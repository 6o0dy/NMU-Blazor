using System.Globalization;
using System.Text;

namespace NMU.Platform.Components.Services;

public class Mp4TimeMap
{
    private readonly long[] _offsets;
    private readonly double[] _times;
    private readonly long[] _syncOffsets;
    private readonly double[] _syncTimes;

    internal Mp4TimeMap(long[] offsets, double[] times, long[] syncOffsets, double[] syncTimes)
    {
        _offsets = offsets;
        _times = times;
        _syncOffsets = syncOffsets;
        _syncTimes = syncTimes;
    }

    public int Count => _offsets.Length;
    public double Duration => _times.Length > 0 ? _times[^1] : 0;
    public int SyncCount => _syncOffsets.Length;

    public double MapTimeAt(long byteOffset)
    {
        if (_offsets.Length == 0) return 0;
        if (byteOffset <= _offsets[0]) return _times[0];
        if (byteOffset >= _offsets[^1]) return _times[^1];

        int lo = 0, hi = _offsets.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_offsets[mid] <= byteOffset) lo = mid;
            else hi = mid;
        }

        var o0 = _offsets[lo];
        var o1 = _offsets[hi];
        if (o1 <= o0) return _times[hi];
        return _times[lo] + (byteOffset - o0) / (double)(o1 - o0) * (_times[hi] - _times[lo]);
    }

    public double GetSyncTimeAtOrAfter(long byteOffset)
    {
        if (_syncOffsets.Length == 0) return MapTimeAt(byteOffset);
        if (byteOffset <= _syncOffsets[0]) return _syncTimes[0];
        if (byteOffset >= _syncOffsets[^1]) return _syncTimes[^1];

        int lo = 0, hi = _syncOffsets.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_syncOffsets[mid] <= byteOffset) lo = mid;
            else hi = mid;
        }
        return _syncOffsets[hi] >= byteOffset ? _syncTimes[hi] : _syncTimes[^1];
    }
}

public static class Mp4SampleTable
{
    private const long ChunkSize = MediaCacheService.ChunkSize;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<int>> ScannedChunkSets = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> MoovCache = new();

    public static void ResetScanCache()
    {
        ScannedChunkSets.Clear();
        MoovCache.Clear();
    }

    public static async Task<Mp4TimeMap?> TryParseFromCacheAsync(string cacheDir, long totalSize)
    {
        if (totalSize <= 0) return null;
        try
        {
            var moov = await ReadMoovBoxAsync(cacheDir, totalSize);
            if (moov == null || moov.Length == 0) return null;
            return BuildMap(moov);
        }
        catch { return null; }
    }

    public static async Task<string> DebugDumpAsync(string cacheDir, long totalSize)
    {
        var sb = new StringBuilder();
        try
        {
            var moov = await ReadMoovBoxAsync(cacheDir, totalSize);
            if (moov == null) return "moov not found";
            var movieTs = ReadMovieTimescale(moov);
            double movieDur = 0;
            foreach (var (t, d) in Boxes(moov, 8, moov.Length))
            {
                if (t != "mvhd" || d.Length < 28) continue;
                var v = d[0];
                var off = v == 1 ? 24 : 16;
                movieDur = (v == 1 ? (long)BE64(d, off) : BE32(d, off)) / (double)movieTs;
            }
            sb.AppendLine($"moov bytes={moov.Length} movieTimescale={movieTs} movieDuration={movieDur:F2}s");
            foreach (var (type, trak) in Boxes(moov, 8, moov.Length))
            {
                if (type != "trak") continue;
                var hdlr = GetHandlerType(trak);
                var mdhd = FindChild(FindChild(trak, "mdia")!, "mdhd");
                var ts = ReadTimescale(mdhd);
                var shift = ReadEditShift(trak, ts, movieTs);
                sb.AppendLine($"trak handler={hdlr} timescale={ts} editShift={shift:F3}s");
                var stbl = FindChild(FindChild(FindChild(trak, "mdia")!, "minf")!, "stbl");
                if (stbl != null)
                {
                    var stts = FindChild(stbl, "stts");
                    var stss = FindChild(stbl, "stss");
                    var ctts = FindChild(stbl, "ctts");
                    var stsz = FindChild(stbl, "stsz");
                    var stsc = FindChild(stbl, "stsc");
                    long totalDur = 0;
                    if (stts != null)
                    {
                        var n = BE32(stts, 4);
                        for (int e = 0; e < n; e++)
                        {
                            var p = 8 + e * 8;
                            if (p + 8 > stts.Length) break;
                            totalDur += (long)BE32(stts, p) * BE32(stts, p + 4);
                        }
                    }
                    sb.AppendLine($"   samples={(stsz != null ? BE32(stsz, 8) : 0)} sttsDur={totalDur / (double)ts:F2}s syncs={(stss != null ? BE32(stss, 4) : -1)} hasCtts={(ctts != null)}");
                }
            }
            try
            {
                var m = BuildMap(moov);
                sb.AppendLine($"MAP: {(m == null ? "null" : $"count={m.Count} sync={m.SyncCount} dur={m.Duration:F2}s")}");
            }
            catch (Exception ex) { sb.AppendLine("BUILDMAP EX: " + ex); }
        }
        catch (Exception ex) { sb.AppendLine("ERR: " + ex.Message); }
        return sb.ToString();
    }

    private static async Task<byte[]?> ReadMoovBoxAsync(string cacheDir, long totalSize)
    {
        if (MoovCache.TryGetValue(cacheDir, out var cached)) return cached;

        var head = await TryReadMoovFromRegionAsync(cacheDir, 0, Math.Min(ChunkSize, totalSize), 0, totalSize);
        if (head != null && head.Length > 0)
        {
            MoovCache.TryAdd(cacheDir, head);
            return head;
        }

        var tailRegion = ChunkSize * MediaCacheService.TailChunkCount(Math.Max(1, (int)(totalSize / ChunkSize)));
        var tailStart = Math.Max(0, totalSize - tailRegion);
        var tail = await TryReadMoovFromRegionAsync(cacheDir, tailStart, Math.Min(tailRegion, totalSize), tailStart, totalSize);
        if (tail != null && tail.Length > 0)
        {
            MoovCache.TryAdd(cacheDir, tail);
            return tail;
        }

        var scanned = ScannedChunkSets.GetOrAdd(cacheDir, _ => new HashSet<int>());
        var candidates = new List<(int Idx, string Path)>();
        foreach (var f in Directory.GetFiles(cacheDir, "chunk_*"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Length <= 6) continue;
            if (!int.TryParse(name[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var idx)) continue;
            if (!scanned.Add(idx)) continue;
            candidates.Add((idx, f));
        }
        if (candidates.Count == 0) return null;

        foreach (var (idx, path) in candidates)
        {
            byte[] data;
            try { data = await File.ReadAllBytesAsync(path); }
            catch { continue; }
            var baseOff = (long)idx * ChunkSize;
            for (int p = 0; p + 8 <= data.Length; p++)
            {
                if (data[p + 4] != (byte)'m' || data[p + 5] != (byte)'o' ||
                    data[p + 6] != (byte)'o' || data[p + 7] != (byte)'v')
                    continue;
                var size = BE32(data, p);
                if (size == 1)
                {
                    if (p + 16 > data.Length) continue;
                    var s64 = BE64(data, p + 8);
                    if (s64 > (ulong)int.MaxValue) continue;
                    size = (uint)s64;
                }
                else if (size == 0)
                {
                    size = (uint)(totalSize - baseOff - p);
                }
                if (size < 8 || baseOff + p + (long)size > totalSize) continue;
                var box = await ReadRangeAsync(cacheDir, baseOff + p, size, totalSize);
                if (box != null && box.Length > 0)
                {
                    MoovCache.TryAdd(cacheDir, box);
                    return box;
                }
                return null;
            }
        }
        return null;
    }

    private static async Task<byte[]?> TryReadMoovFromRegionAsync(string cacheDir, long regionStart, long regionLen, long readStart, long totalSize)
    {
        var region = await ReadRangeAsync(cacheDir, readStart, regionLen, totalSize);
        if (region == null) return null;
        var h = FindMoovHeader(region);
        if (h == null) return null;
        return await ReadRangeAsync(cacheDir, regionStart + h.Value.BoxStart, h.Value.BoxSize, totalSize);
    }

    private static async Task<byte[]?> ReadRangeAsync(string cacheDir, long start, long length, long totalSize)
    {
        var clamped = (int)Math.Min(length, totalSize - start);
        if (clamped <= 0) return null;
        var buf = new byte[clamped];
        long pos = start;
        int written = 0;
        while (written < clamped)
        {
            var chunkIdx = (int)(pos / ChunkSize);
            var file = Path.Combine(cacheDir, $"chunk_{chunkIdx:D6}");
            if (!File.Exists(file)) break;
            var inChunk = (int)(pos % ChunkSize);
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var toRead = (int)Math.Min(clamped - written, fs.Length - inChunk);
            if (toRead <= 0) break;
            fs.Seek(inChunk, SeekOrigin.Begin);
            var n = await fs.ReadAsync(buf, written, toRead);
            if (n <= 0) break;
            written += n;
            pos += n;
        }
        if (written < clamped) Array.Resize(ref buf, written);
        return buf;
    }

    private static (long BoxStart, uint BoxSize)? FindMoovHeader(byte[] buf)
    {
        for (int pos = 0; pos + 8 <= buf.Length; pos += 4)
        {
            if (buf[pos + 4] != (byte)'m' || buf[pos + 5] != (byte)'o' ||
                buf[pos + 6] != (byte)'o' || buf[pos + 7] != (byte)'v')
                continue;
            var size = BE32(buf, pos);
            var hdr = 8;
            if (size == 1)
            {
                if (pos + 16 > buf.Length) continue;
                var s64 = BE64(buf, pos + 8);
                if (s64 > (ulong)int.MaxValue) continue;
                size = (uint)s64;
                hdr = 16;
            }
            else if (size == 0)
            {
                size = (uint)(buf.Length - pos);
            }
            if (size < hdr) continue;
            return (pos, size);
        }
        return null;
    }

    private static Mp4TimeMap? BuildMap(byte[] moov)
    {
        var samples = new List<(long Offset, double Time)>();
        var syncSamples = new List<(long Offset, double Time)>();
        var fileHasVideo = HasHandlerType(moov, "vide");
        var movieTs = ReadMovieTimescale(moov);

        foreach (var (type, trak) in Boxes(moov, 8, moov.Length))
        {
            if (type != "trak") continue;

            var mdia = FindChild(trak, "mdia");
            if (mdia == null) continue;
            var mdhd = FindChild(mdia, "mdhd");
            var minf = FindChild(mdia, "minf");
            if (mdhd == null || minf == null) continue;
            var stbl = FindChild(minf, "stbl");
            if (stbl == null) continue;

            var timescale = ReadTimescale(mdhd);
            if (timescale <= 0) continue;
            var editShift = ReadEditShift(trak, timescale, movieTs);

            var stsc = FindChild(stbl, "stsc");
            var stsz = FindChild(stbl, "stsz");
            var stcoBox = FindChild(stbl, "stco");
            var co64Box = stcoBox == null ? FindChild(stbl, "co64") : null;
            var stco = stcoBox ?? co64Box;
            var stts = FindChild(stbl, "stts");
            if (stsc == null || stsz == null || stco == null || stts == null) continue;
            var co64 = co64Box != null;

            var sampleCount = BE32(stsz, 8);
            if (sampleCount <= 0 || sampleCount > 10_000_000) continue;

            var uniformSize = BE32(stsz, 4);
            var sizes = new long[sampleCount];
            if (uniformSize != 0)
            {
                Array.Fill(sizes, uniformSize);
            }
            else
            {
                for (int s = 0; s < sampleCount; s++)
                {
                    if (12 + s * 4 + 4 > stsz.Length) break;
                    sizes[s] = BE32(stsz, 12 + s * 4);
                }
            }

            var durations = new long[sampleCount];
            var sttsCount = BE32(stts, 4);
            int dIdx = 0;
            for (int e = 0; e < sttsCount && dIdx < sampleCount; e++)
            {
                var p = 8 + e * 8;
                if (p + 8 > stts.Length) break;
                var cnt = BE32(stts, p);
                var delta = BE32(stts, p + 4);
                for (int k = 0; k < cnt && dIdx < sampleCount; k++)
                    durations[dIdx++] = delta;
            }

            var cttsOff = new long[sampleCount];
            var cttsBox = FindChild(stbl, "ctts");
            if (cttsBox != null && cttsBox.Length >= 8)
            {
                var cttsVer = cttsBox[0];
                var cttsCount = BE32(cttsBox, 4);
                int s = 0;
                for (int e = 0; e < cttsCount && s < sampleCount; e++)
                {
                    var p = 8 + e * 8;
                    if (p + 8 > cttsBox.Length) break;
                    var cnt = BE32(cttsBox, p);
                    var off = cttsVer == 1 ? (long)(int)BE32(cttsBox, p + 4) : (long)BE32(cttsBox, p + 4);
                    for (int k = 0; k < cnt && s < sampleCount; k++)
                        cttsOff[s++] = off;
                }
            }

            var isVideoTrack = GetHandlerType(trak) == "vide";
            var isSync = new bool[sampleCount];
            var hasStss = false;
            var stssBox = FindChild(stbl, "stss");
            if (stssBox != null && stssBox.Length >= 8)
            {
                hasStss = true;
                var stssCount = BE32(stssBox, 4);
                for (int e = 0; e < stssCount; e++)
                {
                    var p = 8 + e * 4;
                    if (p + 4 > stssBox.Length) break;
                    var n = (int)BE32(stssBox, p) - 1;
                    if (n >= 0 && n < sampleCount) isSync[n] = true;
                }
            }

            var perChunk = new Dictionary<int, int>();
            var stscCount = BE32(stsc, 4);
            int firstChunk = 1;
            int spc = 0;
            for (int e = 0; e < stscCount; e++)
            {
                var p = 8 + e * 12;
                if (p + 12 > stsc.Length) break;
                var fc = BE32(stsc, p);
                var newSpc = BE32(stsc, p + 4);
                for (int c = firstChunk; c < fc; c++)
                    perChunk[c - 1] = spc;
                spc = (int)newSpc;
                firstChunk = (int)fc;
            }

            var chunkCount = BE32(stco, 4);
            if (chunkCount > 10_000_000) continue;
            var chunkOffsets = new long[chunkCount];
            for (int c = 0; c < chunkCount; c++)
            {
                var p = 8 + (co64 ? c * 8 : c * 4);
                if (p + (co64 ? 8 : 4) > stco.Length) break;
                chunkOffsets[c] = co64 ? (long)BE64(stco, p) : BE32(stco, p);
            }

            long cumTime = 0;
            int sIdx = 0;
            for (int c = 0; c < chunkCount && sIdx < sampleCount; c++)
            {
                var samplesInChunk = perChunk.GetValueOrDefault(c, spc);
                if (samplesInChunk <= 0) continue;
                long offset = chunkOffsets[c];
                for (int s = 0; s < samplesInChunk && sIdx < sampleCount; s++)
                {
                    var t = (cumTime + cttsOff[sIdx]) / (double)timescale + editShift;
                    samples.Add((offset, t));
                    var isSyncSample = isVideoTrack ? (isSync[sIdx] || !hasStss) : !fileHasVideo;
                    if (isSyncSample)
                        syncSamples.Add((offset, t));
                    offset += sizes[sIdx];
                    cumTime += durations[sIdx];
                    sIdx++;
                }
            }
        }

        if (samples.Count == 0) return null;
        samples.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        syncSamples.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var offsets = new long[samples.Count];
        var times = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            offsets[i] = samples[i].Offset;
            times[i] = samples[i].Time;
        }

        var syncOffsets = new long[syncSamples.Count];
        var syncTimes = new double[syncSamples.Count];
        for (int i = 0; i < syncSamples.Count; i++)
        {
            syncOffsets[i] = syncSamples[i].Offset;
            syncTimes[i] = syncSamples[i].Time;
        }

        return new Mp4TimeMap(offsets, times, syncOffsets, syncTimes);
    }

    private static string GetHandlerType(byte[] trak)
    {
        var mdia = FindChild(trak, "mdia");
        if (mdia == null) return "";
        var hdlr = FindChild(mdia, "hdlr");
        if (hdlr == null || hdlr.Length < 12) return "";
        return Encoding.ASCII.GetString(hdlr, 8, 4);
    }

    private static bool HasHandlerType(byte[] moov, string wanted)
    {
        foreach (var (type, trak) in Boxes(moov, 8, moov.Length))
        {
            if (type != "trak") continue;
            if (GetHandlerType(trak) == wanted) return true;
        }
        return false;
    }

    private static byte[]? FindChild(byte[] box, string type)
    {
        foreach (var (t, d) in Boxes(box, 0, box.Length))
            if (t == type) return d;
        return null;
    }

    private static IEnumerable<(string Type, byte[] Data)> Boxes(byte[] data, int start, int end)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var size = BE32(data, pos);
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            var hdr = 8;
            if (size == 1)
            {
                if (pos + 16 > end) break;
                var s64 = BE64(data, pos + 8);
                if (s64 > (ulong)(end - pos)) break;
                size = (uint)s64;
                hdr = 16;
            }
            else if (size == 0)
            {
                size = (uint)(end - pos);
            }
            if (size < hdr || pos + (int)size > end) break;
            yield return (type, data[(pos + hdr)..(pos + (int)size)]);
            pos += (int)size;
        }
    }

    private static int ReadMovieTimescale(byte[] moov)
    {
        foreach (var (t, d) in Boxes(moov, 8, moov.Length))
        {
            if (t != "mvhd" || d.Length < 20) continue;
            var version = d[0];
            var off = version == 1 ? 20 : 12;
            if (off + 4 > d.Length) continue;
            return (int)BE32(d, off);
        }
        return 0;
    }

    private static double ReadEditShift(byte[] trak, int trackTimescale, int movieTimescale)
    {
        if (trackTimescale <= 0) return 0;
        var edts = FindChild(trak, "edts");
        if (edts == null) return 0;
        var elst = FindChild(edts, "elst");
        if (elst == null || elst.Length < 8) return 0;

        var version = elst[0];
        var count = BE32(elst, 4);
        if (count <= 0 || count > 1000) return 0;
        double movieAccSec = 0;
        for (int e = 0; e < count; e++)
        {
            var w = version == 1 ? 20 : 12;
            var p = 8 + e * w;
            if (p + w > elst.Length) break;
            long segDur, mediaTime;
            if (version == 1)
            {
                segDur = (long)BE64(elst, p);
                mediaTime = (long)BE64(elst, p + 8);
            }
            else
            {
                segDur = BE32(elst, p);
                mediaTime = (long)(int)BE32(elst, p + 4);
            }
            if (mediaTime == -1)
            {
                movieAccSec += movieTimescale > 0 ? segDur / (double)movieTimescale : 0;
                continue;
            }
            return movieAccSec - mediaTime / (double)trackTimescale;
        }
        return 0;
    }

    private static int ReadTimescale(byte[] mdhd)
    {
        if (mdhd.Length < 16) return 0;
        var version = mdhd[0];
        var off = version == 1 ? 20 : 12;
        if (off + 4 > mdhd.Length) return 0;
        return (int)BE32(mdhd, off);
    }

    private static uint BE32(byte[] b, int p)
    {
        if (p + 4 > b.Length) return 0;
        return (uint)((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]);
    }

    private static ulong BE64(byte[] b, int p)
    {
        if (p + 8 > b.Length) return 0;
        return ((ulong)BE32(b, p) << 32) | BE32(b, p + 4);
    }
}
