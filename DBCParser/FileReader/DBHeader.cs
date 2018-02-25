using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace DBCParser.FileReader
{
	public class DBHeader : ICloneable
	{
		public string FileName { get; set; }
		public int BuildNumber { get; set; }


		// standard fields
		public string Signature { get; protected set; }
		public uint RecordCount { get; protected set; }
		public uint FieldCount { get; protected set; }
		public uint RecordSize { get; protected set; }
		public uint StringBlockSize { get; protected set; }

		// DB2 fields
		public uint TableHash { get; protected set; }
		public int Build { get; set; }
		public int TimeStamp { get; set; }
		public int MinId { get; protected set; }
		public int MaxId { get; protected set; }
		public int Locale { get; protected set; }
		public int CopyTableSize { get; protected set; } = 0;

		public object Clone()
		{
			return this.MemberwiseClone();
		}

		public virtual void ReadHeader(ref BinaryReader dbReader, string signature)
		{
			this.Signature = signature;
			RecordCount = dbReader.ReadUInt32();
			FieldCount = dbReader.ReadUInt32();
			RecordSize = dbReader.ReadUInt32();
			StringBlockSize = dbReader.ReadUInt32();
		}

	}
}
