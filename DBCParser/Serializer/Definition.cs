using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace DBCParser.Serializer
{
	[Serializable]
	public class Definition
	{
		[XmlElement("Table")]
		public HashSet<Table> Tables { get; set; } = new HashSet<Table>(new TableComparer());

		public Definition(string path)
		{
			XmlSerializer deser = new XmlSerializer(typeof(Definition));
			using (var fs = new FileStream(path, FileMode.Open))
				Tables = ((Definition)deser.Deserialize(fs)).Tables;
		}

		public Definition() { }
	}

	[Serializable]
	public class Table
	{
		[XmlAttribute]
		public string Name { get; set; }
		[XmlAttribute]
		public int Build { get; set; }
		[XmlElement("Field")]
		public List<Field> Fields { get; set; }
	}

	[Serializable]
	public class Field
	{
		[XmlAttribute]
		public string Name { get; set; }
		[XmlAttribute]
		public string Type { get; set; }
		[XmlAttribute, DefaultValue(1)]
		public int ArraySize { get; set; } = 1;
		[XmlAttribute, DefaultValue(false)]
		public bool IsIndex { get; set; } = false;
		[XmlAttribute, DefaultValue(false)]
		public bool AutoGenerate { get; set; } = false;
		[XmlAttribute, DefaultValue(0)]
		public int Padding { get; set; } = 0;
		[XmlAttribute, DefaultValue("")]
		public string DefaultValue { get; set; } = "";
	}

	public class TableComparer : IEqualityComparer<Table>
	{
		public bool Equals(Table x, Table y)
		{
			if (x.Build == y.Build && x.Name == y.Name)
				return true;

			return false;
		}

		public int GetHashCode(Table obj)
		{
			unchecked
			{
				int hash = (int)2166136261;
				hash = (hash * 16777619) ^ obj.Name.GetHashCode();
				hash = (hash * 16777619) ^ obj.Build.GetHashCode();
				hash = (hash * 16777619) ^ obj.Fields.Count.GetHashCode();


				foreach (var f in obj.Fields)
				{
					hash = (hash * 16777619) ^ f.Name.ToLower().GetHashCode();
				}
					

				return hash;
			}
		}
	}
}
