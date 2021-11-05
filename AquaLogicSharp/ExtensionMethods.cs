﻿using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using AquaLogicSharp.Models;

namespace AquaLogicSharp
{
    public static class ExtensionMethods
    {
        public static IEnumerable<byte> ToBytes(this int value, int length, ByteOrder byteOrder)
        {
            var bytes = BitConverter.GetBytes(value); //bytes will be in the order of the system architecture already
            
            //We want the value to be as if it was decoded as big endian for the moment
            if (!BitConverter.IsLittleEndian || byteOrder != ByteOrder.BigEndian) 
                return bytes;
            
            Array.Reverse(bytes);
            return bytes.TakeLast(length);
        }

        public static long FromBytes(this byte[] value, ByteOrder byteOrder)
        {
            return byteOrder == ByteOrder.LittleEndian ? 
                BinaryPrimitives.ReadInt16LittleEndian(value) : 
                BinaryPrimitives.ReadInt16BigEndian(value);
        }

        public static string Hexlify(this byte[] value)
        {
            return BitConverter.ToString(value);
        }
    }
}
