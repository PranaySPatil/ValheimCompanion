using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace JotunnModStub.Companions.Data
{
    // Versioned BinaryWriter codec. Format documented in PHASE_1_WOLF_COMPANION_SPEC.md §3.2.
    internal static class CompanionStateCodec
    {
        public enum DecodeResult
        {
            Ok,
            BadMagic,
            FutureVersion,
            Corrupt
        }

        private const byte FieldAppearanceSeed   = 1;
        private const byte FieldHomePos          = 2;
        private const byte FieldHomeBoundZdoId   = 3;
        private const byte FieldAuditRing        = 4;
        private const byte FieldTameLevel        = 5;
        private const byte FieldAcquisition      = 6;

        public static byte[] Encode(CompanionState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CompanionState.Magic);            // uint16
                w.Write((ushort)CompanionState.SchemaVersion);
                // We'll patch fieldCount after writing the body
                long fieldCountPos = ms.Position;
                w.Write((ushort)0);

                ushort fields = 0;

                WriteAppearance(w, s);         fields++;
                WriteHomePos(w, s);            fields++;
                WriteHomeBound(w, s);          fields++;
                WriteAudit(w, s);              fields++;
                WriteTameLevel(w, s);          fields++;
                WriteAcquisition(w, s);        fields++;

                var end = ms.Position;
                ms.Position = fieldCountPos;
                w.Write(fields);
                ms.Position = end;

                var bytes = ms.ToArray();
                if (bytes.Length > CompanionState.MaxBlobBytes)
                {
                    // Truncate audit ring first to fit the cap.
                    while (s.AuditRing.Count > 0 && bytes.Length > CompanionState.MaxBlobBytes)
                    {
                        s.AuditRing.RemoveAt(0);
                        bytes = Encode(s);
                    }
                }
                return bytes;
            }
        }

        public static DecodeResult TryDecode(byte[] data, out CompanionState state)
        {
            state = new CompanionState();
            if (data == null || data.Length < 6) return DecodeResult.Corrupt;

            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    ushort magic = r.ReadUInt16();
                    if (magic != CompanionState.Magic) return DecodeResult.BadMagic;
                    ushort version = r.ReadUInt16();
                    if (version > CompanionState.SchemaVersion) return DecodeResult.FutureVersion;
                    ushort fieldCount = r.ReadUInt16();

                    for (int i = 0; i < fieldCount; i++)
                    {
                        if (ms.Position >= ms.Length) break;
                        byte fieldId = r.ReadByte();
                        switch (fieldId)
                        {
                            case FieldAppearanceSeed:
                                state.AppearanceSeed = r.ReadInt32();
                                break;
                            case FieldHomePos:
                                state.HomePos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                                break;
                            case FieldHomeBoundZdoId:
                                state.HomeBoundZdoId = r.ReadInt64();
                                break;
                            case FieldAuditRing:
                                int count = r.ReadInt32();
                                state.AuditRing = new List<string>(count);
                                for (int j = 0; j < count; j++)
                                {
                                    state.AuditRing.Add(r.ReadString());
                                }
                                break;
                            case FieldTameLevel:
                                state.TameLevel = r.ReadInt32();
                                break;
                            case FieldAcquisition:
                                state.Acquisition = (AcquisitionKind)r.ReadByte();
                                break;
                            default:
                                // Unknown field — payload format unknown so we must stop.
                                // New fields are always appended; this is the trailer.
                                return DecodeResult.Ok;
                        }
                    }
                }
                return DecodeResult.Ok;
            }
            catch
            {
                return DecodeResult.Corrupt;
            }
        }

        private static void WriteAppearance(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldAppearanceSeed);
            w.Write(s.AppearanceSeed);
        }

        private static void WriteHomePos(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldHomePos);
            w.Write(s.HomePos.x);
            w.Write(s.HomePos.y);
            w.Write(s.HomePos.z);
        }

        private static void WriteHomeBound(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldHomeBoundZdoId);
            w.Write(s.HomeBoundZdoId);
        }

        private static void WriteAudit(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldAuditRing);
            var list = s.AuditRing ?? new List<string>();
            int count = list.Count;
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                var line = list[i] ?? string.Empty;
                if (Encoding.UTF8.GetByteCount(line) > CompanionState.MaxAuditEntryBytes)
                {
                    line = line.Substring(0, Math.Min(line.Length, CompanionState.MaxAuditEntryBytes));
                }
                w.Write(line);
            }
        }

        private static void WriteTameLevel(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldTameLevel);
            w.Write(s.TameLevel);
        }

        private static void WriteAcquisition(BinaryWriter w, CompanionState s)
        {
            w.Write(FieldAcquisition);
            w.Write((byte)s.Acquisition);
        }
    }
}
