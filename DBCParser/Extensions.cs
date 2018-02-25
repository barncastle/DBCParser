using DBCParser.FileReader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DBCParser
{
	public static class Extensions
	{
		public static string Reverse(this string s)
		{
			return new string(s.ToCharArray().Reverse().ToArray());
		}

		public static string ReadStringNull(this BinaryReader reader)
		{
			byte num;
			List<byte> temp = new List<byte>();

			while ((num = reader.ReadByte()) != 0)
				temp.Add(num);

			return Encoding.UTF8.GetString(temp.ToArray());
		}

		public static sbyte[] ReadSByte(this BinaryReader br, int count)
		{
			var arr = new sbyte[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadSByte();

			return arr;
		}

		public static byte[] ReadByte(this BinaryReader br, int count)
		{
			var arr = new byte[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadByte();

			return arr;
		}

		public static int[] ReadInt32(this BinaryReader br, int count)
		{
			var arr = new int[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadInt32();

			return arr;
		}

		public static uint[] ReadUInt32(this BinaryReader br, int count)
		{
			var arr = new uint[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadUInt32();

			return arr;
		}

		public static float[] ReadSingle(this BinaryReader br, int count)
		{
			var arr = new float[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadSingle();

			return arr;
		}

		public static long[] ReadInt64(this BinaryReader br, int count)
		{
			var arr = new long[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadInt64();

			return arr;
		}

		public static ulong[] ReadUInt64(this BinaryReader br, int count)
		{
			var arr = new ulong[count];
			for (int i = 0; i < count; i++)
				arr[i] = br.ReadUInt64();

			return arr;
		}

		public static string ReadString(this BinaryReader br, int count)
		{
			byte[] stringArray = br.ReadBytes(count);
			return Encoding.UTF8.GetString(stringArray);
		}

		public static void Scrub(this BinaryReader br, long pos)
		{
			br.BaseStream.Position = pos;
		}

		public static string ToHex(this byte[] bytes, int max = 32)
		{
			int maxlen = Math.Min(max, bytes.Length);
			char[] c = new char[maxlen * 2];

			int b;
			for (int i = 0; i < maxlen; i++)
			{
				b = bytes[i] >> 4;
				c[i * 2] = (char)(55 + b + (((b - 10) >> 31) & -7));
				b = bytes[i] & 0xF;
				c[i * 2 + 1] = (char)(55 + b + (((b - 10) >> 31) & -7));
			}

			return new string(c);
		}

		public static int ShallowHash<T>(this IEnumerable<T> enumerable)
		{
			unchecked
			{
				int hash = (int)2166136261;
				foreach (var f in enumerable)
					hash = (hash * 16777619) ^ f.GetHashCode();

				return hash;
			}
		}


	}
}
