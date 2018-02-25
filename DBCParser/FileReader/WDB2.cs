using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DBCParser.FileReader
{
	public class WDB2 : WDBC
	{
		public override void ReadHeader(ref BinaryReader dbReader, string signature)
		{
			base.ReadHeader(ref dbReader, signature);

			TableHash = dbReader.ReadUInt32();
			Build = dbReader.ReadInt32();
			TimeStamp = dbReader.ReadInt32();
			MinId = dbReader.ReadInt32();
			MaxId = dbReader.ReadInt32();
			Locale = dbReader.ReadInt32();
			CopyTableSize = dbReader.ReadInt32();

			if (MaxId != 0 && Build > 12880)
			{
				int diff = MaxId - MinId + 1; //Calculate the array sizes
				dbReader.BaseStream.Position += diff * sizeof(int); // skip index map
				dbReader.BaseStream.Position += diff * sizeof(ushort); // skip string lengths
			}
		}
	}
}
