using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using AquaLogicSharp.Models;
using EnumsNET;

namespace AquaLogicSharp
{
    public static class ExtensionMethods
    {
        public static IEnumerable<byte> ToBytes(this int value, int length, ByteOrder byteOrder)
        {
            var bytes = BitConverter.GetBytes(value); //bytes will be in the order of the system architecture

            //Normalize to little endian regardless of the architecture we are running on
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            if (byteOrder != ByteOrder.BigEndian)
                return bytes.Take(length);

            //Big endian wants the least significant bytes last
            Array.Reverse(bytes);
            return bytes.TakeLast(length);
        }

        /// <summary>
        /// Decodes an unsigned integer from the leading bytes of <paramref name="value"/>.
        /// The width follows the length of the span given, so a four byte LED frame decodes
        /// all 32 state bits rather than just the low 16.
        /// </summary>
        public static int FromBytes(this byte[] value, ByteOrder byteOrder)
        {
            return value.Length switch
            {
                //unchecked so the top bit lands in the sign bit rather than throwing
                >= 4 => unchecked((int)(byteOrder == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(value)
                    : BinaryPrimitives.ReadUInt32BigEndian(value))),
                >= 2 => byteOrder == ByteOrder.LittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(value)
                    : BinaryPrimitives.ReadUInt16BigEndian(value),
                1 => value[0],
                _ => 0
            };
        }

        public static string Hexlify(this byte[] value)
        {
            return BitConverter.ToString(value);
        }

        public static bool Is(this State state, State matchState)
        {
            return state == matchState;
        }
        
        public static string[]? ToStateArray(this State state)
        {
            if (state == State.EMPTY)
                return default;
            
            return state.ToString()
                .Split(new string[] { ", " }, StringSplitOptions.None)
                .Select(i => Enum.Parse<State>(i).GetName()!)
                .ToArray();
        }
    }
}
