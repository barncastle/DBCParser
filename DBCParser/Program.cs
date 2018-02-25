using Dapper;
using DBCParser.Comparer;
using DBCParser.FileReader;
using DBCParser.Serializer;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace DBCParser
{
	class Program
	{
		public static IEnumerable<string> Directories;
		public static Definition KnownDefinitions;

		public static readonly string Output = @"D:\DBCDump\_Definitions";
		public static readonly string BasePath = @"D:\DBCDump";

		static void Main(string[] args)
		{
			KnownDefinitions = new Definition(@"Definitions\3368.xml");
			KnownDefinitions.Tables.UnionWith(new Definition(@"Definitions\11927.xml").Tables);
			KnownDefinitions.Tables.UnionWith(new Definition(@"Definitions\18179.xml").Tables);
			KnownDefinitions.Tables.UnionWith(new Definition(@"Definitions\various.xml").Tables);

			Directories = Directory.EnumerateDirectories(BasePath);

			//Export();

			//using (ZipArchive zipArchive = ZipFile.Open(@"D:\Stage1.zip", ZipArchiveMode.Create))
			//	Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories).ToList()
			//		.ForEach(x => zipArchive.CreateEntryFromFile(x, Path.GetFileName(x)));

			//RowLevelCompare();
			//GroupedRowLevelCompare();

			//using (ZipArchive zipArchive = ZipFile.Open(@"D:\Stage2.zip", ZipArchiveMode.Create))
			//	Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories).ToList()
			//		.ForEach(x => zipArchive.CreateEntryFromFile(x, Path.GetFileName(x)));

			// compare files
			Compare();

			Console.WriteLine("LANGREF " + DBReader.LangBuild);
			Console.ReadLine();
		}

		private static void Export()
		{
			Console.WriteLine("Started Exporting.");
			XmlSerializer xml = new XmlSerializer(typeof(DBEntry[]));

			List<DBEntry> Entries = new List<DBEntry>();

			// parse DB files
			using (var sw = new StreamWriter(Path.Combine(Output, "error.txt")))
			{
				var dirs = Directory.EnumerateDirectories(BasePath).ToList();
				dirs.RemoveAll(x => x.Contains("_Definitions"));

				dirs.Sort((x, y) =>
				{
					var di1 = new DirectoryInfo(x).Name.ToLower().Split('.');
					var di2 = new DirectoryInfo(y).Name.ToLower().Split('.');

					int comp = 0;
					for (int i = 0; i < di1.Length; i++)
					{
						comp = int.Parse(di1[i].Replace("a", "")).CompareTo(int.Parse(di2[i].Replace("a", "")));
						if (comp != 0)
							return comp;
					}

					return 0;
				});

				foreach (var dir in dirs)
				{
					int build = int.Parse(new DirectoryInfo(dir).Name.Split('.').Last());

					var files = Directory.EnumerateFiles(dir, "*.db*", SearchOption.TopDirectoryOnly);
					foreach (var file in files)
					{
						var entry = DBReader.Read(file, build, out string error);
						if (entry != null)
						{
							Entries.Add(entry);
						}
						if (!string.IsNullOrWhiteSpace(error))
						{
							Console.WriteLine($" ERR:: {Path.GetFileName(file)} {build} - " + error);
							sw.WriteLine($"{Path.GetFileName(file)} {build} - " + error);
						}
					}

					Console.WriteLine($"Exported {build}");
				}
			}


			// save now to avoid any mishaps
			foreach (var entries in Entries.GroupBy(x => Path.GetFileNameWithoutExtension(x.Name)).Select(x => x.ToList()))
				using (Stream file = File.Create(Path.Combine(Output, Path.GetFileNameWithoutExtension(entries[0].Name.ToLower()) + ".xml")))
					xml.Serialize(file, entries.ToArray());

			Console.WriteLine("Finished Exporting.");
		}

		private static void RowLevelCompare()
		{
			Console.WriteLine("Started Raw Row comparison.");

			XmlSerializer xml = new XmlSerializer(typeof(DBEntry[]));
			var xmls = Directory.EnumerateFiles(Output, "*.xml").ToList();

			//int indexCur = xmls.FindIndex(x => x.ToLower().EndsWith("characterloadoutitem.xml"));
			//if (indexCur > -1)
			//	xmls.RemoveRange(0, indexCur + 1);

			foreach (var f in xmls)
			{
				List<DBEntry> entries;

				using (var fs = File.OpenRead(f))
					entries = ((DBEntry[])xml.Deserialize(fs)).ToList();

				// sort build numbers + fix formatting
				foreach (var entry in entries)
				{
					entry.Builds.Sort((a, b) => a.CompareTo(b));
					entry.Name = entry.Name.ToUpper();
					entry.Fields.ForEach(x => x.Name = x.Name.ToUpper());
				}
				entries.Sort((a, b) => a.Builds.Min().CompareTo(b.Builds.Min()));

				// ID column never moves until WDB5
				if (entries.Any(x => x.Fields.Count > 0 && x.Fields[0].Name == "ID"))
				{
					foreach (var entry in entries)
						if (entry.Fields.Count > 0 && entry.Fields[0].Type == "INT")
							entry.Fields[0].Name = "ID";
				}

				// row checksum compare - work backwards as matching entries are grouped down
				for (int i = entries.Count - 1; i > 0; i--)
					if (entries[i - 1].Builds.Count > 0 && entries[i].Builds.Count > 0)
						DBComparer.RowLevelMatch(entries[i - 1], entries[i]);

				// remove all invalid entries
				entries.RemoveAll(x => x.Builds.Count == 0);

				// save
				using (Stream file = File.Create(f))
				{
					entries.ForEach(x => x.Builds.Sort((a, b) => a.CompareTo(b)));
					entries.Sort((a, b) => a.Builds.Min().CompareTo(b.Builds.Min()));
					xml.Serialize(file, entries.ToArray());
				}

				Console.WriteLine($"Compared {Path.GetFileName(f)}");
			}

			Console.WriteLine("Finished Raw Row comparison.");
		}

		private static void GroupedRowLevelCompare()
		{
			Console.WriteLine("Started Grouped Raw Row comparison.");

			XmlSerializer xml = new XmlSerializer(typeof(DBEntry[]));
			var xmls = Directory.EnumerateFiles(Output, "*.xml").ToList();

			//int indexCur = xmls.FindIndex(x => x.ToLower().EndsWith("characterloadoutitem.xml"));
			//if (indexCur > -1)
			//	xmls.RemoveRange(0, indexCur + 1);

			foreach (var f in xmls)
			{
				List<DBEntry> entries;

				using (var fs = File.OpenRead(f))
					entries = ((DBEntry[])xml.Deserialize(fs)).ToList();

				// ignore 1 entry
				if (entries.Count == 1)
					continue;

				// sort build numbers
				entries.ForEach(x => x.Builds.Sort((a, b) => a.CompareTo(b)));
				entries.Sort((a, b) => a.Builds.Min().CompareTo(b.Builds.Min()));

				// group by field types and compare
				var groups = entries.GroupBy(x => x.Fields.Select(y => y.Type).ShallowHash()).Where(x => x.Count() > 1);
				foreach (var group in groups)
					for (int i = group.Count() - 1; i > 0; i--)
						if (group.ElementAt(i - 1).Builds.Count > 0 && group.ElementAt(i).Builds.Count > 0)
							DBComparer.RowLevelMatch(group.ElementAt(i - 1), group.ElementAt(i));

				// remove all invalid entries
				entries.RemoveAll(x => x.Builds.Count == 0);

				// save
				using (Stream file = File.Create(f))
				{
					entries.ForEach(x => x.Builds.Sort((a, b) => a.CompareTo(b)));
					entries.Sort((a, b) => a.Builds.Min().CompareTo(b.Builds.Min()));
					xml.Serialize(file, entries.ToArray());
				}

				Console.WriteLine($"Compared {Path.GetFileName(f)}");
			}

			Console.WriteLine("Finished Raw Row comparison.");
		}

		private static void Compare()
		{
			var xmls = Directory.EnumerateFiles(Output, "*.xml");
			foreach (var f in xmls)
				DBComparer.Compare(f);

			// create _log.csv
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("FILENAME,COMPLETION %");

			foreach (var r in DBComparer.Completion)
				sb.AppendLine($"{r.Key},{r.Value.ToString("#000.00")}%");

			sb.AppendLine("");
			sb.AppendLine($"TOTAL,{ DBComparer.Completion.Average(x => x.Value).ToString("000.00")}%");

			File.WriteAllText(Path.Combine(Output, "_Log.csv"), sb.ToString());
		}


		private static bool PatchKnown(string name, int build, Definition definition, List<DBField> fields)
		{
			Table table = definition.Tables.FirstOrDefault(x => x.Name.ToUpper() == name.ToUpper() && x.Build == build);

			// doesn't exist
			if (table == null)
				return false;

			// wrong column count?
			if (table.Fields.Where(x => !x.AutoGenerate).Sum(x => x.ArraySize) != fields.Count)
				return false;

			int cnt = 0;
			foreach (var field in table.Fields)
			{
				if (field.AutoGenerate)
					continue;
				if (string.IsNullOrWhiteSpace(field.Name))
					continue;

				for (int i = 0; i < field.ArraySize; i++)
				{
					DBField entry = new DBField
					{
						Name = (field.ArraySize <= 1 ? field.Name : field.Name + (i + 1)).ToUpper()
					};

					switch (field.Type.Trim().ToLower().TrimStart('u'))
					{
						case "loc":
							entry.Type = "LANGSTRINGREF";
							break;
						case "short":
							entry.Type = "USHORT";
							break;
						case "int":
						case "float":
						case "string":
						case "byte":
						case "long":
							entry.Type = field.Type.ToUpper();
							break;
					}

					fields[cnt] = entry;
					cnt++;
				}
			}

			return true;
		}


	}


}
