// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization.Formatters.Binary;
using Xunit;

namespace System
{
    public static class BinarySerialization
    {
        public static void EnsureSerializableAttribute(Assembly assemblyUnderTest, Dictionary<string, bool> serializableTypes)
        {
            foreach (Type type in assemblyUnderTest.GetTypes())
            {
                var serializableAttribute = Attribute.GetCustomAttribute(type, typeof(SerializableAttribute));

                // Ensure that all types required by known serialization scenarions 
                // and only these types are decorated with the SerializableAttribute.
                if (serializableTypes.ContainsKey(type.FullName))
                {
                    Assert.NotNull(serializableAttribute);
                    serializableTypes[type.FullName] = true;
                }
                else
                {
                    Assert.True(null == serializableAttribute, $"Serializable attribute is not expected on {type.FullName}");
                }
            }

            foreach (KeyValuePair<string, bool> entry in serializableTypes)
            {
                Assert.True(entry.Value, $"Did we remove {entry.Key}?");
            }
        }

        public static object EnsureDeserialize(string blob)
        {
            var @object = FromBase64String(blob);
            Assert.NotNull(@object);
            return @object;
        }

        public static string ToBase64String(object @object,
            FormatterAssemblyStyle assemblyStyle = FormatterAssemblyStyle.Simple)
        {
            byte[] raw = ToByteArray(@object, assemblyStyle);
            return Convert.ToBase64String(raw);
        }

        private static object FromBase64String(string base64String,
            FormatterAssemblyStyle assemblyStyle = FormatterAssemblyStyle.Simple)
        {
            byte[] raw = Convert.FromBase64String(base64String);
            return FromByteArray(raw, assemblyStyle);
        }

        private static object FromByteArray(byte[] raw,
            FormatterAssemblyStyle assemblyStyle = FormatterAssemblyStyle.Simple)
        {
            var binaryFormatter = new BinaryFormatter
            {
                AssemblyFormat = assemblyStyle
            };

            using (var serializedStream = new MemoryStream(raw))
            {
                return binaryFormatter.Deserialize(serializedStream);
            }
        }

        private static byte[] ToByteArray(object obj,
            FormatterAssemblyStyle assemblyStyle = FormatterAssemblyStyle.Simple)
        {
            var binaryFormatter = new BinaryFormatter
            {
                AssemblyFormat = assemblyStyle
            };

            using (MemoryStream ms = new MemoryStream())
            {
                binaryFormatter.Serialize(ms, obj);
                return ms.ToArray();
            }
        }
    }
}
