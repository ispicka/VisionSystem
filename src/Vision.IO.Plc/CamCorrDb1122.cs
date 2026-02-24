// CamCorrDb1122.cs
// S7.NetPlus mapping for DB1122 ReservedForPublic (byte 114+)
// Contains TWO variants:
//  1) CamCorrDb1122_Simple  - direct per-read/per-write with read-modify-write of bytes
//  2) CamCorrDb1122_Batch   - fast batch read (114..127) + local cache, writes only DBB114 for bits
//
// Target: S7-300 + DB1122
//
// Addresses (as per your design):
//  RPi -> PLC (DBX114.*):
//   DBX114.0 Enable
//   DBX114.1 Req
//   DBX114.2 ModeAutoAllowed
//   DBX114.3 LeftPlus
//   DBX114.4 LeftMinus
//   DBX114.5 RightPlus
//   DBX114.6 RightMinus
//   DBX114.7 Reset
//
//  PLC -> RPi (DBX115.*):
//   DBX115.0 PlcReady
//   DBX115.1 Ack
//   DBX115.2 Busy
//   DBX115.3 Done
//   DBX115.4 NOK
//   DBX115.5 Timeout
//   DBX115.6 Conflict
//   DBX115.7 Reserved
//
// Optional parameters:
//   DBD116 REAL StepMm
//   DBW120 INT  MaxStepsPerReq  (or implement DINT at DBD120 if you prefer)
//   DBD124 TIME ReqTimeout      (TIME = DINT milliseconds)

using System;
using System.Diagnostics;
using System.Threading;
using S7.Net;

namespace Kern.PlcCamCorr
{
    // ============================================================
    // Variant 1: SIMPLE (per-bit read/write; safe RMW byte)
    // ============================================================
    public sealed class CamCorrDb1122_Simple
    {
        private readonly Plc _plc;
        public int Db { get; }

        private const int IN_BYTE = 114;   // DBX114.*
        private const int OUT_BYTE = 115;  // DBX115.*

        public CamCorrDb1122_Simple(Plc plc, int db = 1122)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            Db = db;
        }

        // ---------- low-level byte/bit ----------
        private byte ReadByte(int byteOffset)
            => ((byte[])_plc.ReadBytes(DataType.DataBlock, Db, byteOffset, 1))[0];

        private void WriteByte(int byteOffset, byte value)
            => _plc.WriteBytes(DataType.DataBlock, Db, byteOffset, new[] { value });

        private static bool GetBit(byte b, int bit) => ((b >> bit) & 0x01) == 1;

        private static byte SetBit(byte b, int bit, bool value)
            => value ? (byte)(b | (1 << bit)) : (byte)(b & ~(1 << bit));

        private bool ReadBool(int byteOffset, int bit)
            => GetBit(ReadByte(byteOffset), bit);

        private void WriteBoolRmw(int byteOffset, int bit, bool value)
        {
            var b = ReadByte(byteOffset);          // read-modify-write byte
            b = SetBit(b, bit, value);
            WriteByte(byteOffset, b);
        }

        private void PulseBool(int byteOffset, int bit, int ms)
        {
            WriteBoolRmw(byteOffset, bit, true);
            Thread.Sleep(ms);
            WriteBoolRmw(byteOffset, bit, false);
        }

        // ---------- REAL / INT / TIME ----------
        public float ReadReal(int byteOffset)
        {
            var bytes = (byte[])_plc.ReadBytes(DataType.DataBlock, Db, byteOffset, 4);
            return S7.Net.Types.Real.FromByteArray(bytes);
        }

        public void WriteReal(int byteOffset, float value)
        {
            var bytes = S7.Net.Types.Real.ToByteArray(value);
            _plc.WriteBytes(DataType.DataBlock, Db, byteOffset, bytes);
        }

        public short ReadInt(int byteOffset)
        {
            var bytes = (byte[])_plc.ReadBytes(DataType.DataBlock, Db, byteOffset, 2);
            return S7.Net.Types.Int.FromByteArray(bytes);
        }

        public int ReadTimeMs(int byteOffset) // TIME = DINT ms
        {
            var bytes = (byte[])_plc.ReadBytes(DataType.DataBlock, Db, byteOffset, 4);
            return S7.Net.Types.DInt.FromByteArray(bytes);
        }

        // ==========================================================
        // RPi -> PLC  (DBX114.*)
        // ==========================================================
        public bool Enable
        {
            get => ReadBool(IN_BYTE, 0);
            set => WriteBoolRmw(IN_BYTE, 0, value);
        }

        public bool Req
        {
            get => ReadBool(IN_BYTE, 1);
            set => WriteBoolRmw(IN_BYTE, 1, value);
        }

        public bool ModeAutoAllowed
        {
            get => ReadBool(IN_BYTE, 2);
            set => WriteBoolRmw(IN_BYTE, 2, value);
        }

        public void PulseLeftPlus(int ms = 80)   => PulseBool(IN_BYTE, 3, ms);
        public void PulseLeftMinus(int ms = 80)  => PulseBool(IN_BYTE, 4, ms);
        public void PulseRightPlus(int ms = 80)  => PulseBool(IN_BYTE, 5, ms);
        public void PulseRightMinus(int ms = 80) => PulseBool(IN_BYTE, 6, ms);

        public void PulseReset(int ms = 120) => PulseBool(IN_BYTE, 7, ms);

        // ==========================================================
        // PLC -> RPi  (DBX115.*)  READ-ONLY
        // ==========================================================
        public bool PlcReady => ReadBool(OUT_BYTE, 0);
        public bool Ack      => ReadBool(OUT_BYTE, 1);
        public bool Busy     => ReadBool(OUT_BYTE, 2);
        public bool Done     => ReadBool(OUT_BYTE, 3);
        public bool Nok      => ReadBool(OUT_BYTE, 4);
        public bool Timeout  => ReadBool(OUT_BYTE, 5);
        public bool Conflict => ReadBool(OUT_BYTE, 6);

        // ==========================================================
        // Optional params
        // ==========================================================
        public float StepMm
        {
            get => ReadReal(116);   // DBD116
            set => WriteReal(116, value);
        }

        public short MaxStepsPerReq_Int => ReadInt(120);     // DBW120
        public int ReqTimeoutMs         => ReadTimeMs(124);  // DBD124 (TIME)
    }

    // ============================================================
    // Variant 2: BATCH (fast read 114..127 + local cache; writes only DBB114)
    // ============================================================
    public sealed class CamCorrDb1122_Batch
    {
        private readonly Plc _plc;
        public int Db { get; }

        private const int BASE = 114;   // start byte
        private const int LEN  = 14;    // bytes 114..127 inclusive

        // cache
        private byte _in114_cache;
        private byte _out115_cache;
        private float _stepMm_cache;
        private short _maxSteps_cache;
        private int _timeoutMs_cache;

        public CamCorrDb1122_Batch(Plc plc, int db = 1122)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            Db = db;
        }

        private static bool GetBit(byte b, int bit) => ((b >> bit) & 0x01) == 1;
        private static byte SetBit(byte b, int bit, bool value)
            => value ? (byte)(b | (1 << bit)) : (byte)(b & ~(1 << bit));

        /// <summary>Reads DB bytes 114..127 (14 bytes) and updates cache.</summary>
        public void Refresh()
        {
            var buf = (byte[])_plc.ReadBytes(DataType.DataBlock, Db, BASE, LEN);

            _in114_cache  = buf[0]; // 114
            _out115_cache = buf[1]; // 115

            // 116..119 => buf[2..5] (REAL)
            var tmp4 = new byte[4];
            Array.Copy(buf, 2, tmp4, 0, 4);
            _stepMm_cache = S7.Net.Types.Real.FromByteArray(tmp4);

            // 120..121 => buf[6..7] (INT)
            var tmp2 = new byte[2];
            Array.Copy(buf, 6, tmp2, 0, 2);
            _maxSteps_cache = S7.Net.Types.Int.FromByteArray(tmp2);

            // 124..127 => buf[10..13] (TIME = DINT ms)
            Array.Copy(buf, 10, tmp4, 0, 4);
            _timeoutMs_cache = S7.Net.Types.DInt.FromByteArray(tmp4);

        }

        // ===========================
        // Read-only view (from cache)
        // ===========================
        public bool PlcReady => GetBit(_out115_cache, 0);
        public bool Ack      => GetBit(_out115_cache, 1);
        public bool Busy     => GetBit(_out115_cache, 2);
        public bool Done     => GetBit(_out115_cache, 3);
        public bool Nok      => GetBit(_out115_cache, 4);
        public bool Timeout  => GetBit(_out115_cache, 5);
        public bool Conflict => GetBit(_out115_cache, 6);

        public float StepMm => _stepMm_cache;
        public short MaxSteps => _maxSteps_cache;
        public int ReqTimeoutMs => _timeoutMs_cache;

        // ===================================
        // Write bits to DBX114.* via cache
        // ===================================
        public void SetEnable(bool v)          => WriteInBit(0, v);
        public void SetReq(bool v)             => WriteInBit(1, v);
        public void SetModeAutoAllowed(bool v) => WriteInBit(2, v);

        public void PulseLeftPlus(int ms = 80)   => PulseInBit(3, ms);
        public void PulseLeftMinus(int ms = 80)  => PulseInBit(4, ms);
        public void PulseRightPlus(int ms = 80)  => PulseInBit(5, ms);
        public void PulseRightMinus(int ms = 80) => PulseInBit(6, ms);
        public void PulseReset(int ms = 120)     => PulseInBit(7, ms);

        private void WriteInBit(int bit, bool value)
        {
            // Uses local cache; writes only DBB114 (1 byte)
            _in114_cache = SetBit(_in114_cache, bit, value);
            _plc.WriteBytes(DataType.DataBlock, Db, 114, new[] { _in114_cache });
        }

        private void PulseInBit(int bit, int ms)
        {
            WriteInBit(bit, true);
            Thread.Sleep(ms);
            WriteInBit(bit, false);
        }

        // ===================================
        // Optional: write StepMm (DBD116)
        // ===================================
        public void WriteStepMm(float stepMm)
        {
            var bytes = S7.Net.Types.Real.ToByteArray(stepMm);
            _plc.WriteBytes(DataType.DataBlock, Db, 116, bytes);
            _stepMm_cache = stepMm; // update local cache
        }
    }

    // ============================================================
    // Shared: handshake helper (works with BOTH variants)
    // ============================================================
    public static class CamCorrHandshake
    {
        public enum Direction
        {
            LeftPlus,
            LeftMinus,
            RightPlus,
            RightMinus
        }

        /// <summary>
        /// Executes ONE correction transaction (Req -> Ack -> pulse direction -> Done) with overall timeout.
        /// For Simple variant.
        /// </summary>
        public static bool ExecuteOneStep(CamCorrDb1122_Simple db, Direction dir, int overallTimeoutMs = 2000)
        {
            db.PulseReset(120);
            var sw = Stopwatch.StartNew();

            db.Req = true;

            // Wait Ack or error
            while (sw.ElapsedMilliseconds < overallTimeoutMs)
            {
                if (db.Nok || db.Timeout || db.Conflict) { db.Req = false; return false; }
                if (db.Ack) break;
                Thread.Sleep(20);
            }
            if (!db.Ack) { db.Req = false; return false; }

            // Pulse direction
            PulseDir(db, dir);

            // Wait Done or error
            while (sw.ElapsedMilliseconds < overallTimeoutMs)
            {
                if (db.Nok || db.Timeout || db.Conflict) { db.Req = false; return false; }
                if (db.Done) break;
                Thread.Sleep(20);
            }

            db.Req = false;
            return db.Done && !(db.Nok || db.Timeout || db.Conflict);
        }

        private static void PulseDir(CamCorrDb1122_Simple db, Direction dir)
        {
            switch (dir)
            {
                case Direction.LeftPlus:   db.PulseLeftPlus(80); break;
                case Direction.LeftMinus:  db.PulseLeftMinus(80); break;
                case Direction.RightPlus:  db.PulseRightPlus(80); break;
                case Direction.RightMinus: db.PulseRightMinus(80); break;
                default: throw new ArgumentOutOfRangeException(nameof(dir), dir, null);
            }
        }

        /// <summary>
        /// Executes ONE correction transaction with batch variant (uses Refresh polling).
        /// </summary>
        public static bool ExecuteOneStep(CamCorrDb1122_Batch db, Direction dir, int overallTimeoutMs = 2000, int pollMs = 10)
        {
            db.Refresh();
            db.PulseReset(120);

            var sw = Stopwatch.StartNew();
            db.SetReq(true);

            // Wait Ack or error (Refresh polling)
            while (sw.ElapsedMilliseconds < overallTimeoutMs)
            {
                db.Refresh();
                if (db.Nok || db.Timeout || db.Conflict) { db.SetReq(false); return false; }
                if (db.Ack) break;
                Thread.Sleep(pollMs);
            }
            db.Refresh();
            if (!db.Ack) { db.SetReq(false); return false; }

            // Pulse direction
            PulseDir(db, dir);

            // Wait Done or error
            while (sw.ElapsedMilliseconds < overallTimeoutMs)
            {
                db.Refresh();
                if (db.Nok || db.Timeout || db.Conflict) { db.SetReq(false); return false; }
                if (db.Done) break;
                Thread.Sleep(pollMs);
            }

            db.SetReq(false);
            db.Refresh();
            return db.Done && !(db.Nok || db.Timeout || db.Conflict);
        }

        private static void PulseDir(CamCorrDb1122_Batch db, Direction dir)
        {
            switch (dir)
            {
                case Direction.LeftPlus:   db.PulseLeftPlus(80); break;
                case Direction.LeftMinus:  db.PulseLeftMinus(80); break;
                case Direction.RightPlus:  db.PulseRightPlus(80); break;
                case Direction.RightMinus: db.PulseRightMinus(80); break;
                default: throw new ArgumentOutOfRangeException(nameof(dir), dir, null);
            }
        }
    }

    // ============================================================
    // Minimal demo (optional): comment out if not needed
    // ============================================================
    public static class Demo
    {
        public static void RunSimpleExample()
        {
            using var plc = new Plc(CpuType.S7300, "192.168.0.10", 0, 2);
            plc.Open();

            var db = new CamCorrDb1122_Simple(plc, 1122);
            var ok = CamCorrHandshake.ExecuteOneStep(db, CamCorrHandshake.Direction.RightPlus, 2000);

            Console.WriteLine($"Simple RightPlus OK = {ok}");
            plc.Close();
        }

        public static void RunBatchExample()
        {
            using var plc = new Plc(CpuType.S7300, "192.168.0.10", 0, 2);
            plc.Open();

            var db = new CamCorrDb1122_Batch(plc, 1122);
            var ok = CamCorrHandshake.ExecuteOneStep(db, CamCorrHandshake.Direction.RightPlus, 2000, 10);

            Console.WriteLine($"Batch RightPlus OK = {ok}");
            plc.Close();
        }
    }
}
