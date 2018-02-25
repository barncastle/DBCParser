using DBCParser.Serializer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DBCParser
{
	public class Entry
	{
		public uint Id { get; set; }
		public string Name { get; set; }
		public string Magic { get; set; }
		public uint RecordCount { get; set; }
		public uint FieldCount { get; set; }
		public uint RecordSize { get; set; }
		public uint StringTableSize { get; set; }
		public uint Build { get; set; }
		public string Checksum { get; set; }
		public string FirstRowChecksum { get; set; }

		public IList<FieldType> Fields { get; set; }
	}

	public class TempEntry
	{
		public string Name { get; set; }
		public int Build { get; set; }
		public SortedDictionary<int, string> Fields { get; set; }

		public TempEntry() { }
		public TempEntry(string name, int build)
		{
			Name = name;
			Build = build;
			Fields = new SortedDictionary<int, string>();
		}
	}

}
