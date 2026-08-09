[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path

if (-not ('LimitLens.ReleaseAudit.BundleInspector' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace LimitLens.ReleaseAudit
{
    public static class BundleInspector
    {
        private static readonly byte[] Signature = new byte[]
        {
            0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
            0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
            0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
            0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
        };

        public static string[] FindPrivatePathEntries(string path)
        {
            byte[] bundle = File.ReadAllBytes(path);
            var findings = new List<string>();
            if (ContainsPrivatePath(bundle))
            {
                findings.Add("<apphost>");
            }

            int signatureOffset = IndexOf(bundle, Signature);
            if (signatureOffset < sizeof(long))
            {
                throw new InvalidDataException("The published executable is not a supported .NET single-file bundle.");
            }

            long headerOffset = BitConverter.ToInt64(bundle, signatureOffset - sizeof(long));
            if (headerOffset < 0 || headerOffset >= bundle.LongLength)
            {
                throw new InvalidDataException("The .NET bundle header offset is invalid.");
            }

            using (var stream = new MemoryStream(bundle, false))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = headerOffset;
                uint majorVersion = reader.ReadUInt32();
                reader.ReadUInt32();
                int fileCount = reader.ReadInt32();
                reader.ReadString();
                if (majorVersion >= 2)
                {
                    for (int index = 0; index < 5; index++)
                    {
                        reader.ReadInt64();
                    }
                }

                if (fileCount < 0 || fileCount > 10000)
                {
                    throw new InvalidDataException("The .NET bundle file count is invalid.");
                }

                for (int index = 0; index < fileCount; index++)
                {
                    long offset = reader.ReadInt64();
                    long size = reader.ReadInt64();
                    long compressedSize = majorVersion >= 6 ? reader.ReadInt64() : 0;
                    reader.ReadByte();
                    string relativePath = reader.ReadString();

                    if (!relativePath.StartsWith("LimitLens", StringComparison.OrdinalIgnoreCase) &&
                        !relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    long storedSize = compressedSize > 0 ? compressedSize : size;
                    if (offset < 0 || storedSize < 0 || offset + storedSize > bundle.LongLength ||
                        offset > int.MaxValue || storedSize > int.MaxValue)
                    {
                        throw new InvalidDataException("A .NET bundle entry is outside the executable bounds.");
                    }

                    byte[] content;
                    if (compressedSize > 0)
                    {
                        using (var compressed = new MemoryStream(bundle, (int)offset, (int)compressedSize, false))
                        using (var inflater = new DeflateStream(compressed, CompressionMode.Decompress))
                        using (var expanded = new MemoryStream())
                        {
                            inflater.CopyTo(expanded);
                            content = expanded.ToArray();
                        }
                    }
                    else
                    {
                        content = new byte[(int)size];
                        Buffer.BlockCopy(bundle, (int)offset, content, 0, (int)size);
                    }

                    if (ContainsPrivatePath(content))
                    {
                        findings.Add(relativePath);
                    }
                }
            }

            return findings.ToArray();
        }

        private static bool ContainsPrivatePath(byte[] data)
        {
            return ContainsAsciiIgnoreCase(data, @":\Users\") ||
                   ContainsAsciiIgnoreCase(data, "/Users/") ||
                   ContainsAsciiIgnoreCase(data, "/home/") ||
                   IndexOf(data, System.Text.Encoding.Unicode.GetBytes(@":\Users\")) >= 0 ||
                   IndexOf(data, System.Text.Encoding.Unicode.GetBytes("/Users/")) >= 0 ||
                   IndexOf(data, System.Text.Encoding.Unicode.GetBytes("/home/")) >= 0;
        }

        private static bool ContainsAsciiIgnoreCase(byte[] data, string value)
        {
            byte[] pattern = System.Text.Encoding.ASCII.GetBytes(value);
            for (int offset = 0; offset <= data.Length - pattern.Length; offset++)
            {
                int index = 0;
                for (; index < pattern.Length; index++)
                {
                    byte left = data[offset + index];
                    byte right = pattern[index];
                    if (left >= (byte)'A' && left <= (byte)'Z') left = (byte)(left + 32);
                    if (right >= (byte)'A' && right <= (byte)'Z') right = (byte)(right + 32);
                    if (left != right) break;
                }

                if (index == pattern.Length) return true;
            }

            return false;
        }

        private static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int offset = 0; offset <= data.Length - pattern.Length; offset++)
            {
                int index = 0;
                for (; index < pattern.Length && data[offset + index] == pattern[index]; index++)
                {
                }

                if (index == pattern.Length) return offset;
            }

            return -1;
        }
    }
}
'@
}

$findings = [LimitLens.ReleaseAudit.BundleInspector]::FindPrivatePathEntries($executable)
if ($findings.Count -gt 0) {
    throw "Release executable contains private build-path metadata in: $($findings -join ', ')"
}

Write-Output "Bundle privacy audit passed: no private build paths found in Limit Lens entries."
