using System;
using System.Buffers.Binary;

namespace AquaLogicSharp
{
    public static class ExtensionMethods
    {
        public static byte[] ToBytes(this int value, int length, ByteOrder byteOrder)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            
            if (byteOrder == ByteOrder.LittleEndian)
                Array.Reverse(bytes);
            
            return bytes;
        }

        public static int FromBytes(this byte[] value, ByteOrder byteOrder)
        {
            if (byteOrder == ByteOrder.LittleEndian)
                return BinaryPrimitives.ReadInt16LittleEndian(value);

            return BinaryPrimitives.ReadInt16BigEndian(value);
        }

        public static string Hexlify(this byte[] value)
        {
            return BitConverter.ToString(value);
        }
    }
}
