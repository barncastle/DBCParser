using DBCParser.FileReader;
using DBCParser.Serializer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBCParser.Comparer
{
	public class RawFile : IDisposable
	{
		public DBEntry Entry { get; set; }
		public Dictionary<int, string> StringTable { get; set; }
		public List<byte[]> RawRecords { get; set; }

		public SortedDictionary<double, double[]> Records { get; set; }
		public int IDIndex { get; set; }


		public void LoadRecords(int build)
		{
			Records = new SortedDictionary<double, double[]>();
			double[] Ids = new double[RawRecords.Count];

			IDIndex = Entry?.Fields.FindIndex(x => x.Name.ToUpper() == "ID") ?? -1;
			if (IDIndex > -1 && RawRecords[0].Length >= 4)
			{
				for (int i = 0; i < RawRecords.Count; i++)
					Ids[i] = BitConverter.ToUInt32(RawRecords[i], IDIndex * 4);

				// fix incorrect id columns
				if (Ids.Distinct().Count() != RawRecords.Count)
				{
					Entry.Fields[IDIndex].Name = "";
					IDIndex = -1;
					Ids = Enumerable.Range(0, RawRecords.Count).Select(x => (double)x).ToArray();
				}
			}
			else
			{
				Ids = Enumerable.Range(0, RawRecords.Count).Select(x => (double)x).ToArray();
			}

			for (int i = 0; i < RawRecords.Count; i++)
			{
				var record = RawRecords[i];

				List<double> array = new List<double>();
				int offset = 0;
				for (int s = 0; s < Entry.Fields.Count; s++)
				{
					if (s == IDIndex)
					{
						offset += 4;
						continue;
					}

					switch (Entry.Fields[s].Type)
					{
						case "FLOAT":
							array.Add(BitConverter.ToSingle(record, offset));
							offset += 4;
							break;
						case "BYTE":
							array.Add(record[offset]);
							offset += 1;
							break;
						case "USHORT":
							array.Add(BitConverter.ToUInt16(record, offset));
							offset += 2;
							break;
						case "LANGSTRINGREF":
							array.Add(BitConverter.ToInt32(record, offset)); // enGB enUS only
							offset += 4;

							int skip = 0;
							while (skip == 0)
							{
								offset += 4;
								skip = BitConverter.ToInt32(record, offset);
							}
							offset += 4;

							break;
						case "ULONG":
							array.Add(BitConverter.ToUInt64(record, offset));
							offset += 8;
							break;
						default:
							array.Add(BitConverter.ToInt32(record, offset));
							offset += 4;
							break;
					}
				}

				Records.Add(Ids[i], array.ToArray());
			}
		}

		public string GetString(int id)
		{
			if (StringTable.ContainsKey(id))
				return StringTable[id];
			else
				return "T2$^@A"; // unpossible value
		}


		public void Dispose()
		{
			Entry = null;
			StringTable?.Clear();
			StringTable = null;
			RawRecords?.Clear();
			RawRecords?.TrimExcess();
			RawRecords = null;
			Records?.Clear();
			Records = null;
		}
	}
}
