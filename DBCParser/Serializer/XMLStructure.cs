using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace DBCParser.Serializer
{
	[Serializable]
	[XmlType("Entry")]
	public class DBEntry
	{
		public string Name { get; set; }
		[XmlArrayItem("Build")]
		public List<int> Builds { get; set; } = new List<int>();
		[XmlArrayItem("Field")]
		public List<DBField> Fields { get; set; } = new List<DBField>();

		public DBEntry() { }
		public DBEntry(string name, int build, List<DBField> fields)
		{
			Name = name;
			Builds = new List<int>() { build };
			Fields = fields;
		}
	}

	[Serializable]
	public class DBField : ICloneable
	{
		[XmlAttribute("Type")]
		public string Type { get; set; }
		[XmlAttribute("Name")]
		public string Name { get; set; }

		public DBField() { }
		public DBField(string type, string name)
		{
			Type = type;
			Name = name;
		}

		public object Clone() => MemberwiseClone();
	}
}
