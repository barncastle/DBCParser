using DBCParser.Comparer;
using DBCParser.Serializer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DBCParser.FileReader
{
	public static class DBReader
	{
		public const double FLOAT_THRESHOLD = 0.85;
		public static int LangBuild = int.MaxValue;

		private static DBHeader ReadHeader(BinaryReader br, string dbFile, int build)
		{
			DBHeader header = null;
			string signature = br.ReadString(4);

			if (string.IsNullOrWhiteSpace(signature))
				return null;

			if (signature[0] != 'W')
				signature = signature.Reverse();

			switch (signature)
			{
				case "WDBC":
					header = new WDBC();
					break;
				case "WDB2":
					header = new WDB2();
					break;
			}

			if (header != null)
			{
				header.ReadHeader(ref br, signature);
			}
			else
			{
				header = new WDBC();
			}

			if (build > 0)
			{
				header.BuildNumber = build;
				header.FileName = Path.GetFileName(dbFile).ToUpper();
			}

			return header;
		}

		public static DBEntry Read(string dbFile, int build, out string error)
		{
			error = "";

			using (var fs = new FileStream(dbFile, FileMode.Open, FileAccess.Read))
			using (var br = new BinaryReader(fs, Encoding.UTF8))
			{
				DBHeader header = ReadHeader(br, dbFile, build);
				if (IsKnown(header, out DBEntry known))
					return known;

				if (!ValidationChecks(header, dbFile, out error))
					return null;

				if (header.RecordSize / header.FieldCount != 4)// dbc has byte column
				{
					error = "Has byte columns.";
					return new DBEntry()
					{
						Name = Path.GetFileName(dbFile).ToUpper(),
						Builds = new List<int>() { build },
						Fields = new List<DBField>()
					};
				}

				Dictionary<int, string> stringTable = new Dictionary<int, string>();
				List<byte[]> dataTable = new List<byte[]>();
				FieldInfo[] fieldInfo = new FieldInfo[header.FieldCount];
				bool hasStrings = false;
				int nullStrings = 0;

				// stringtable stuff
				long pos = br.BaseStream.Position;
				long stringTableStart = br.BaseStream.Position += header.RecordCount * header.RecordSize;
				stringTable = ReadStringTable(br, stringTableStart); //Get stringtable
				br.Scrub(pos);

				// if 1 or 2 empty strings only then strings aren't unused
				hasStrings = !(stringTable.Values.Count <= 2 && stringTable.Values.All(x => string.IsNullOrWhiteSpace(x)));
				// count empties
				nullStrings = stringTable.Count(x => string.IsNullOrWhiteSpace(x.Value));

				// read data				
				for (int i = 0; i < header.RecordCount; i++)
					dataTable.Add(br.ReadBytes((int)header.RecordSize));

				// compute possible types
				for (int i = 0; i < header.FieldCount; i++)
				{
					// no pdb dbc struct has uint fields ITS ALL A LIE!!
					List<FieldType> options = new List<FieldType>() { FieldType.INT, /*FieldType.UINT,*/ FieldType.FLOAT, FieldType.STRING };
					if (!hasStrings)
						options.Remove(FieldType.STRING); // strings not used

					List<int> intVals = new List<int>();
					List<string> stringVals = new List<string>();
					List<float> floatVals = new List<float>();

					for (int r = 0; r < dataTable.Count; r++)
					{
						byte[] data = dataTable[r].Skip(i * 4).Take(4).ToArray();

						// ignore 0 byte columns as they could be anything
						if (data.All(x => x == 0))
							continue;

						// int value
						int asInt = BitConverter.ToInt32(data, 0);
						intVals.Add(asInt);

						// string check
						if (options.Contains(FieldType.STRING))
						{
							if (!stringTable.ContainsKey(asInt))
							{
								options.Remove(FieldType.STRING);
								stringVals.Clear(); // 100% not a string!
							}
							else
							{
								stringVals.Add(stringTable[asInt]);
							}
						}

						// float check
						if (options.Contains(FieldType.FLOAT) && FloatUtil.IsLikelyFloat(asInt))
						{
							floatVals.Add(BitConverter.ToSingle(data, 0));
						}

						// uint check - prefer signed over unsigned as per the wow client
						if (options.Contains(FieldType.UINT) && asInt < 0)
						{
							options.Remove(FieldType.UINT);
						}
					}

					fieldInfo[i] = new FieldInfo()
					{
						IsEmpty = intVals.Count == 0,
						FloatPercentage = (floatVals.Count / (float)intVals.Count) * 100f, // % of valid floats
						Options = options,
						UniqueStrings = stringVals.Distinct().Count(),
						UniqueInts = intVals.Distinct().Count()
					};
				}

				// calculate field types
				List<FieldType> temp = new List<FieldType>();
				for (int i = 0; i < fieldInfo.Length; i++)
				{
					var info = fieldInfo[i];

					if (info.IsEmpty) // all records are 0
					{
						// most likely to be int, less likely to be float, very unlikely to be a string
						temp.Add(info.Options.Contains(FieldType.UINT) ? FieldType.UINT : FieldType.INT);
					}
					else if (info.Options.Contains(FieldType.FLOAT) && info.FloatPercentage > FLOAT_THRESHOLD) // threshold needs tweaking?
					{
						temp.Add(FieldType.FLOAT); // high % of valid floats
					}
					else if (info.Options.Contains(FieldType.STRING) && info.UniqueStrings > 0)
					{
						// 1 string, 1st field is more likely an ID not a string
						if (stringTable.Count - nullStrings < header.FieldCount && header.RecordCount == 1)
						{
							if (i == 0)
								temp.Add(info.Options.Contains(FieldType.UINT) ? FieldType.UINT : FieldType.INT);
							else
								temp.Add(FieldType.STRING);
						}
						else if (info.UniqueStrings == 1)
						{
							// very unlikely to have a column with the same string in every row if there is 
							temp.Add(info.Options.Contains(FieldType.UINT) ? FieldType.UINT : FieldType.INT);
						}
						else
						{
							temp.Add(FieldType.STRING); // case of 0 = "" and 1 = "" in stringtable
						}
					}
					else
					{
						temp.Add(info.Options.Contains(FieldType.UINT) ? FieldType.UINT : FieldType.INT); // uint over int
					}

					// LANGREFSTRING check
					if (temp[temp.Count - 1] == FieldType.STRING)
					{
						if (IsLangStringRef(fieldInfo, i + 1, build, out int offset))
						{
							temp[temp.Count - 1] = FieldType.LANGSTRINGREF;
							i += offset;
						}
					}
				}

				return new DBEntry()
				{
					Name = Path.GetFileName(dbFile).ToUpper(),
					Builds = new List<int>() { build },
					Fields = temp.Select(x => new DBField() { Name = "", Type = x.ToString().ToUpper() }).ToList()
				};
			}

		}

		public static bool IsKnown(DBHeader header, out DBEntry entry)
		{
			entry = null;

			var def = Program.KnownDefinitions.Tables.FirstOrDefault(x => x.Build == header.BuildNumber && x.Name.ToUpper() == Path.GetFileNameWithoutExtension(header.FileName));
			if (def != null)
			{
				entry = new DBEntry()
				{
					Name = header.FileName,
					Builds = new List<int>() { header.BuildNumber },
					Fields = new List<DBField>()
				};

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

				return entry.Fields.Count == header.FieldCount;
			}

			return false;
		}



		public static RawFile ReadRaw(string dbFile, DBEntry entry)
		{
			// figure out what file version exists
			string db2file = Path.ChangeExtension(dbFile, ".db2");
			if (File.Exists(db2file))
				dbFile = db2file;
			else
				dbFile = Path.ChangeExtension(dbFile, ".dbc");

			using (var fs = new FileStream(dbFile, FileMode.Open, FileAccess.Read))
			using (var br = new BinaryReader(fs, Encoding.UTF8))
			{
				DBHeader header = ReadHeader(br, "", 0);
				if (!ValidationChecks(header, dbFile, out string error))
					return null;

				RawFile rawFile = new RawFile() { Entry = entry };

				// stringtable stuff
				long pos = br.BaseStream.Position;
				long stringTableStart = br.BaseStream.Position += header.RecordCount * header.RecordSize;
				rawFile.StringTable = ReadStringTable(br, stringTableStart); //Get stringtable
				br.Scrub(pos);

				// store data		
				rawFile.RawRecords = new List<byte[]>();
				for (int i = 0; i < header.RecordCount; i++)
					rawFile.RawRecords.Add(br.ReadBytes((int)header.RecordSize));

				return rawFile;
			}
		}



		private static bool ValidationChecks(DBHeader header, string FileName, out string error)
		{
			error = string.Empty;
			string name = Path.GetFileName(FileName) + " " + Directory.GetParent(FileName).Name;

			if (header == null)
			{
				Console.WriteLine(name + ": Not a dbc.");
				error = "Not a dbc.";
				return false;
			}

			if (header.RecordCount == 0 || header.RecordSize == 0)
			{
				Console.WriteLine(name + ": No records.");
				error = "No records.";
				return false;
			}

			return true;
		}

		private static Dictionary<int, string> ReadStringTable(BinaryReader dbReader, long stringTableStart)
		{
			Dictionary<int, string> table = new Dictionary<int, string>();

			if (dbReader.BaseStream.Position > dbReader.BaseStream.Length)
				return table;

			while (dbReader.BaseStream.Position < dbReader.BaseStream.Length)
			{
				int index = (int)(dbReader.BaseStream.Position - stringTableStart);
				table[index] = dbReader.ReadStringNull(); //Extract all the strings to the string table
			}

			return table;
		}

		private static bool IsLangStringRef(IList<FieldInfo> list, int start, int build, out int count)
		{
			// 8 locale check <= TBC 2.0.?
			var nextfields = list.Skip(start).Take(8);
			if (nextfields.Count() == 8)
			{
				var areempty = nextfields.Take(7).All(x => x.IsEmpty);
				var hasmask = nextfields.Last().Options.Any(x => x == FieldType.UINT || x == FieldType.INT) && !nextfields.Last().IsEmpty;

				if (areempty && hasmask)
				{
					count = 8;
					return true;
				}
			}

			// 16 locale check
			nextfields = list.Skip(start).Take(16);
			if (nextfields.Count() == 16)
			{
				var areempty = nextfields.Take(15).All(x => x.IsEmpty);
				var hasmask = nextfields.Last().Options.Any(x => x == FieldType.UINT || x == FieldType.INT) && !nextfields.Last().IsEmpty;

				if (areempty && hasmask)
				{
					LangBuild = Math.Min(build, LangBuild);

					count = 16;
					return true;
				}
			}

			count = 0;
			return false;
		}

		internal class FieldInfo
		{
			public List<FieldType> Options = new List<FieldType>();
			public float FloatPercentage = 0f;
			public int UniqueInts;
			public int UniqueStrings;
			public bool IsEmpty;
		}
	}
}
