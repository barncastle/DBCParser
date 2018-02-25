using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DBCParser.FileReader
{
	public static class MetaExtractor
	{
		static readonly int[] TypeSize = new int[] { 4, 8, 4, 4, 1, 2 };


		public static void Extract(string file)
		{
			byte[] data = File.ReadAllBytes(file);
			string stringStream = Encoding.ASCII.GetString(data);

			var matches = Regex.Matches(stringStream, @"(DBFilesClient\\[a-zA-Z0-9]*\.db(c|2))\0", RegexOptions.Compiled);
			Dictionary<string, long> offsets = new Dictionary<string, long>();
			foreach (Match match in matches)
				offsets.Add(match.Groups[0].Value, match.Index);

			stringStream = null;
			matches = null;

			List<DBFile> dbFiles = new List<DBFile>();

			using (var fs = File.OpenRead(file))
			using (var br = new BinaryReader(fs))
			{
				foreach (var offset in offsets)
				{
					bool isDB2 = offset.Key.EndsWith(".db2\0");

					br.BaseStream.Position = offset.Value + offset.Key.Length;
					if (offset.Key.Length % 4 != 0)
						br.BaseStream.Position += (4 - offset.Key.Length % 4);  // align to 4th byte

					long pos = br.BaseStream.Position;

					List<int> ints = ParsePreMop(br, isDB2, out int fieldcount, out int recordsize);
					if (ints.Skip((ints.Count / 3) * 2).Any(x => x < 0 || x > 5))
					{
						br.BaseStream.Position = pos;
						ints = ParsePostMop(br, isDB2, out fieldcount, out recordsize);
					}

					FieldInfo[] fields = Enumerable.Range(0, ints.Count / 3).Select(x => new FieldInfo()).ToArray();
					for (int i = 0; i < fields.Length; i++)
					{
						fields[i].Offset = ints[i];
						fields[i].Size = ints[i + fields.Length];
						fields[i].Type = ints[i + fields.Length * 2];

						if (fields[i].Type < 0 || fields[i].Type > 5)
							throw new ArgumentOutOfRangeException();

						// fix offsets
						if (i > 0)
							fields[i].Offset = fields[i - 1].Offset + (fields[i - 1].Size * TypeSize[fields[i - 1].Type]);
					}

					Array.Resize(ref fields, fieldcount);

					// check for missing fields
					if (fields.Any(x => x == null))
					{
						Console.WriteLine($"MISSING META {offset.Key}");
						continue;
					}

					int fieldSize = fields.Sum(x => Math.Max(TypeSize[x.Type], x.Size));
					int actualSize = fields.Sum(x => x.Size);
					if (fieldSize != recordsize && actualSize > recordsize)
					{
						throw new Exception("SIZE MISMATCH");
					}

					DBFile db = new DBFile()
					{
						Name = offset.Key.Split('\\')[1].Split('.')[0],
						FieldCount = fieldcount,
						RecordSize = recordsize,
						Fields = fields,
						Verify = (fieldSize != recordsize && actualSize < recordsize)
					};

					dbFiles.Add(db);
					Console.WriteLine(db.ToString());
				}
			}

			dbFiles.Sort((x, y) => x.Name.CompareTo(y.Name));
			using (var sw = new StreamWriter("dump.txt", false))
				foreach (var db in dbFiles)
					sw.WriteLine(db.ToString());

			Console.ReadLine();
		}

		private static List<int> ParsePreMop(BinaryReader br, bool isDB2, out int fieldcount, out int recordsize)
		{
			int temp = 0;
			List<int> ints = new List<int>();
			while ((temp = br.ReadInt32()) < 10000000)
				ints.Add(temp);
			if (ints.Count % 3 != 0)
				ints.AddRange(new int[(3 - ints.Count % 3)]); // fix padding


			int[] data = Enumerable.Range(0, 5).Select(x => br.ReadInt32()).ToArray();
			if (isDB2)
			{
				fieldcount = data[1];
				recordsize = data[2];
			}
			else
			{
				fieldcount = data[0];
				recordsize = data[1];
			}

			if (ints[1] != 0 && isDB2 && ints.Count > fieldcount * 3)
				ints.RemoveRange(fieldcount * 3, ints.Count - (fieldcount * 3));

			return ints;
		}


		private static List<int> ParsePostMop(BinaryReader br, bool isDB2, out int fieldcount, out int recordsize)
		{
			int temp = 0;
			List<int> ints = new List<int>();
			while ((temp = br.ReadInt32()) < 10000000)
				ints.Add(temp);

			if (isDB2)
			{
				br.ReadInt32();
				fieldcount = br.ReadInt32();
				recordsize = br.ReadInt32();
				Enumerable.Range(0, 10).Select(x => br.ReadInt32()).ToArray(); // dump
			}
			else
			{
				fieldcount = br.ReadInt32();
				recordsize = br.ReadInt32();
				Enumerable.Range(0, 8).Select(x => br.ReadInt32()).ToArray(); // dump
			}


			int count = ints.Count * 3;
			temp = 0;
			while ((temp = br.ReadInt32()) < 10000000 && ints.Count < count)
				ints.Add(temp);
			if (ints.Count < count)
				ints.AddRange(new int[count - ints.Count]);


			return ints;
		}


		internal class DBFile
		{
			public string Name;
			public int FieldCount;
			public int RecordSize;
			public bool Verify;

			public FieldInfo[] Fields;

			public override string ToString()
			{
				StringBuilder sb = new StringBuilder();
				sb.AppendLine($"struct {Name}Rec // sizeof(0x{RecordSize.ToString("X")}), {FieldCount} fields {(Verify ? "VERIFY" : "")}");
				sb.AppendLine("{");
				foreach (var f in Fields)
				{
					sb.Append("   ");
					sb.AppendLine(f.ToString());
				}
				sb.AppendLine("};");
				return sb.ToString();
			}
		}

		internal class FieldInfo
		{
			public int Offset;
			public int Type;
			public int Size;

			public override string ToString()
			{
				int size = Math.Max(1, Size / TypeSize[Type]);
				return $"{(FieldType)Type} field{Offset.ToString("X3")}" + (size > 1 ? $"[{size}]" : "") + $" // size {Size}, type {Type}";
			}
		}

	}
}
