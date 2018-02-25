using DBCParser.FileReader;
using DBCParser.Serializer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DBCParser.Comparer
{
	public static class DBComparer
	{
		public const double MATCH_THRESHOLD = 0.95;
		public const double STRING_MATCH_THRESHOLD = 0.9;
		public const double TAKEWHILE_THRESHOLD = 0.8;
		public static Dictionary<string, float> Completion = new Dictionary<string, float>();


		/// <summary>
		/// Compares two DBCs by [ID_Column] with a % threshold of same values to name unknown fields.
		/// </summary>
		/// <param name="path"></param>
		public static void Compare(string path)
		{
			List<DBEntry> entries;
			XmlSerializer xml = new XmlSerializer(typeof(DBEntry[]));
			string filename = "";

			using (Stream file = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
				entries = new List<DBEntry>(xml.Deserialize(file) as DBEntry[]);

			if (entries.Count <= 1) // no entries
			{
				LogCompletion(entries);
				entries = null;
				return;
			}

			Console.Write("Comparing " + Path.GetFileNameWithoutExtension(path) + "...");

			filename = entries[0].Name;
			entries.ForEach(x => x.Builds.Sort((a, b) => a.CompareTo(b)));
			entries.Sort((x, y) => x.Builds.Min().CompareTo(y.Builds.Min())); // sort by builds

			foreach (var entry in entries)
			{
				var def = Program.KnownDefinitions.Tables.FirstOrDefault(x => entry.Builds.Contains(x.Build) && x.Name.ToUpper() == Path.GetFileNameWithoutExtension(entry.Name));
				if (def != null)
				{
					entry.Fields.Clear();

					foreach (var field in def.Fields)
					{
						if (field.AutoGenerate)
							continue;
						if (string.IsNullOrWhiteSpace(field.Name))
							continue;

						for (int i = 0; i < field.ArraySize; i++)
						{
							DBField fieldEntry = new DBField
							{
								Name = (field.ArraySize <= 1 ? field.Name : field.Name + (i + 1)).ToUpper()
							};

							switch (field.Type.Trim().ToLower().TrimStart('u'))
							{
								case "loc":
									fieldEntry.Type = "LANGSTRINGREF";
									break;
								case "short":
									fieldEntry.Type = "USHORT";
									break;
								case "int":
								case "float":
								case "string":
								case "byte":
								case "long":
									fieldEntry.Type = field.Type.ToUpper();
									break;
							}

							entry.Fields.Add(fieldEntry);
						}
					}
				}
			}

			// these are fucked some weird DBC/DB2 hybrid used in WoD alpha
			string[] skip = new string[] { "groupfindercategory", "garrplotuicategory" };
			if (skip.Any(x => filename.ToLower().Contains(x)))
				return;

			// compare logic
			CompareCore(ref entries, filename); // compare old to new
			entries.Reverse();
			CompareCore(ref entries, filename); // compare new to old
			entries.Reverse();

			using (Stream file = File.Create(path))
			{
				// final formatting and save
				entries.RemoveAll(x => x.Builds.Count == 0);
				entries.ForEach(x => x.Builds.Sort((a, b) => a.CompareTo(b)));
				entries.Sort((x, y) => x.Builds.Min().CompareTo(y.Builds.Min()));
				xml.Serialize(file, entries.ToArray());

				LogCompletion(entries);
				entries = null;
			}

			Console.WriteLine(" DONE");
		}

		private static void CompareCore(ref List<DBEntry> entries, string filename)
		{
			for (int i = 1; i < entries.Count; i++)
			{
				var builds = ClosestBuilds(entries[i - 1], entries[i]);
				var previous = DBReader.ReadRaw(GetFileName(builds.Item1, filename), entries[i - 1]);
				var current = DBReader.ReadRaw(GetFileName(builds.Item2, filename), entries[i]);

				if (previous == null || current == null)
					continue;

				try
				{
					previous.LoadRecords(builds.Item1);
					current.LoadRecords(builds.Item2);
				}
				catch
				{
					continue;
				}				

				// only work with files both having an ID or no-ID
				if ((previous.IDIndex == -1 && current.IDIndex > -1) || (previous.IDIndex > -1 && current.IDIndex == -1))
					continue;
				// ignore fully matched entries
				if (!current.Entry.Fields.Any(x => string.IsNullOrWhiteSpace(x.Name)))
					continue;

				// set up ID information
				var ids = previous.Records.Keys.Intersect(current.Records.Keys); // get matching IDs
				int idCount = ids.Count();
				int idOffset = previous.IDIndex == -1 ? 0 : 1;

				// record known columns so we skip them
				string[] known = current.Entry.Fields.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).ToArray();

				// STEP 1 COMPARE EVERY POSSIBLE COLUMN, those matching threshold values are assigned a name
				Parallel.For(0, previous.Entry.Fields.Count, x =>
				{
					// skip id columns
					if (x == previous.IDIndex)
						return;
					// skip unknown columns
					if (string.IsNullOrWhiteSpace(previous.Entry.Fields[x].Name))
						return;
					// already has been matched
					if (known.Contains(previous.Entry.Fields[x].Name))
						return;

					bool isString = previous.Entry.Fields[x].Type.Contains("STRING");

					//for (int y = 0; y < current.Entry.Fields.Count; y++)
					Parallel.For(0, current.Entry.Fields.Count, y =>
					{
						// skip id columns
						if (y == current.IDIndex)
							return;
						// same name or unknown
						if (previous.Entry.Fields[x].Name == current.Entry.Fields[y].Name)
							return;
						// already has been matched
						if (!string.IsNullOrWhiteSpace(current.Entry.Fields[y].Name))
							return;

						// avoid mainly zero filled cloumns
						var zeros = (float)Math.Max(ids.Count(id => current.Records[id][y - idOffset] == 0), 1) / ids.Count();
						if (zeros < MATCH_THRESHOLD)
						{
							int matching = 0;

							if (isString)
							{
								matching = ids.Count(id => previous.GetString((int)previous.Records[id][x - idOffset]) == current.GetString((int)current.Records[id][y - idOffset]));
							}
							else
							{
								matching = ids.Count(id => previous.Records[id][x - idOffset] == current.Records[id][y - idOffset]);
							}

							if (matching > 0 && ((float)matching / idCount) >= (isString ? STRING_MATCH_THRESHOLD : MATCH_THRESHOLD))
								current.Entry.Fields[y] = (DBField)previous.Entry.Fields[x].Clone();

							if (current.Entry.Fields[y].Type == "LANGREFSTRING" && current.Entry.Builds.Any(b => b >= 11927))
								current.Entry.Fields[y].Type = "STRING";
						}
					});
				});

				// STEP 2 MERGE, combine fully matched entries
				if (AreMatchingFields(current.Entry.Fields, previous.Entry.Fields))
				{
					previous.Entry.Builds.AddRange(current.Entry.Builds.ToList());
					current.Entry.Builds.Clear();
					entries.Remove(current.Entry);
				}
				else
				{
					// STEP 3 REMOVE DUPLICATES, anything that matched more than once
					var duplicates = current.Entry.Fields.GroupBy(x => x.Name).Where(x => x.Count() > 1).Select(x => x.Key);
					if (duplicates.Any())
						current.Entry.Fields.ForEach(x => x.Name = (duplicates.Contains(x.Name) ? "" : x.Name));
				}

				previous?.Dispose();
				previous = null;
				current?.Dispose();
				current = null;
			}
		}

		/// <summary>
		/// Compares two DBCs at byte level to find any matching rows.
		/// </summary>
		/// <param name="entry1"></param>
		/// <param name="entry2"></param>
		public static bool RowLevelMatch(DBEntry entry1, DBEntry entry2)
		{
			var builds = ClosestBuilds(entry1, entry2);
			var previous = DBReader.ReadRaw(GetFileName(builds.Item1, entry1.Name), entry1);
			var current = DBReader.ReadRaw(GetFileName(builds.Item2, entry2.Name), entry2);

			if (previous == null || current == null)
				return false;

			if (previous.RawRecords[0].Length != current.RawRecords[0].Length)
				return false;

			if (previous.Entry.Fields.Count != current.Entry.Fields.Count)
				return false;

			int size = previous.RawRecords[0].Length - 1;

			previous.RawRecords.Sort(new ByteListComparer());
			current.RawRecords.Sort(new ByteListComparer());

			var pRecs = previous.RawRecords.Select(x => x);
			var cRecs = current.RawRecords.Select(x => x);

			bool ismatch = pRecs.AsParallel().Any(x => cRecs.Where(y => y[0] == x[0] && y[size] == x[size]).Any(y => y.SequenceEqual(x))); // check for any byte-for-byte records
			if (ismatch)
			{
				entry1.Builds.AddRange(entry2.Builds.ToArray()); // combine backwards

				for (int i = 0; i < entry2.Fields.Count; i++)
					if (!string.IsNullOrWhiteSpace(entry2.Fields[i].Name))
						entry1.Fields[i] = (DBField)entry2.Fields[i].Clone();

				entry2.Builds.Clear();
			}

			// cleanup
			previous?.Dispose();
			previous = null;
			current?.Dispose();
			current = null;

			return ismatch;
		}


		#region Helpers
		private static string GetFileName(int build, string name)
		{
			if (Program.Directories.Any(x => x.EndsWith(build.ToString())))
				return Path.Combine(Program.BasePath, Program.Directories.First(x => x.EndsWith(build.ToString())), name);

			return string.Empty;
		}

		private static Tuple<int, int> ClosestBuilds(DBEntry entry1, DBEntry entry2)
		{
			int build1 = -1, build2 = -1;
			int difference = int.MaxValue;

			foreach (var build in entry1.Builds)
			{
				var nearest = entry2.Builds.Select(p => new { Build = p, Difference = Math.Abs(p - build) }).OrderBy(p => p.Difference).First();
				if (nearest.Difference < difference)
				{
					build1 = build;
					build2 = nearest.Build;
				}
			}

			return new Tuple<int, int>(build1, build2);
		}

		private static void LogCompletion(IEnumerable<DBEntry> entries)
		{
			if (!entries.Any())
				return;

			// stats
			float done = entries.Sum(x => x.Fields.Count(y => !string.IsNullOrWhiteSpace(y.Name)));
			float total = entries.Sum(x => x.Fields.Count);
			Completion.Add(entries.First().Name, (done / total) * 100f);
		}
		#endregion

		#region Row comparers
		public static bool AreMatchingFields(List<DBField> list1, List<DBField> list2)
		{
			if (list1.Count != list2.Count)
				return false;
			if (list1.Select(x => x.Type).ShallowHash() != list2.Select(x => x.Type).ShallowHash())
				return false;
			if (list1.Select(x => x.Name).ShallowHash() != list2.Select(x => x.Name).ShallowHash())
				return false;

			return true;
		}
		#endregion
	}


	public class ByteListComparer : IComparer<IList<byte>>
	{
		public int Compare(IList<byte> x, IList<byte> y)
		{
			int result = x.Count.CompareTo(y.Count);
			if (result != 0)
				return result;

			for (int index = 0; index < x.Count; index++)
			{
				result = x[index].CompareTo(y[index]);
				if (result != 0)
					return result;
			}

			return result;
		}
	}
}
