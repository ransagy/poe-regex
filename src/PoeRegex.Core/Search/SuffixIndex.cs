namespace PoeRegex.Core.Search;

/// <summary>
/// Suffix-array index over a set of lines used for
/// shortest-unique-substring fingerprinting and the false-positive oracle.
///
/// Lines may contain a placeholder character (<see cref="DigitPlaceholder"/>)
/// that stands for a digit-run; comparisons treat it as a single token so
/// fingerprints stay value-agnostic across tiers.
/// </summary>
public sealed class SuffixIndex
{
    /// <summary>Placeholder inserted in place of every digit-run when normalizing lines.</summary>
    public const char DigitPlaceholder = '\x01';

    private readonly int _n;
    private readonly int[] _s;
    private readonly int[] _sa;
    private readonly int[] _rank;
    private readonly int[] _lcp;
    private readonly int[] _lineOfRank;
    private readonly int[] _prevDiff;
    private readonly int[] _nextDiff;
    private readonly SparseTable _rmq;
    private readonly int[] _lineStart;
    private readonly int[] _lineLength;
    private readonly string[] _corpus;
    private readonly bool _caseInsensitive;

    private SuffixIndex(
        int[] s, int[] sa, int[] rank, int[] lcp, int[] lineOfRank,
        int[] prevDiff, int[] nextDiff, SparseTable rmq, int[] lineStart, int[] lineLength,
        string[] corpus, bool caseInsensitive)
    {
        _n = s.Length;
        _s = s;
        _sa = sa;
        _rank = rank;
        _lcp = lcp;
        _lineOfRank = lineOfRank;
        _prevDiff = prevDiff;
        _nextDiff = nextDiff;
        _rmq = rmq;
        _lineStart = lineStart;
        _lineLength = lineLength;
        _corpus = corpus;
        _caseInsensitive = caseInsensitive;
    }

    /// <summary>The lines the index was built over (original case).</summary>
    public IReadOnlyList<string> ClassCorpus => _corpus;

    public static SuffixIndex Build(IReadOnlyList<string> lines) => Build(lines, caseInsensitive: false);

    /// <summary>
    /// Builds the index over the given lines. When <paramref name="caseInsensitive"/>
    /// is true, uniqueness and matching are computed on the case-folded text, so a
    /// returned fingerprint is unique under case-insensitive matching (and hence also
    /// under case-sensitive matching). Fingerprints are always returned in the
    /// original case of the source line.
    /// </summary>
    public static SuffixIndex Build(IReadOnlyList<string> lines, bool caseInsensitive)
    {
        // Alphabet: code points in 0..0x10FFFF, per-line terminators above that.
        const int terminatorBase = 1 << 21;

        var fold = caseInsensitive ? FoldLines(lines) : lines;

        int[] lineStart = new int[fold.Count];
        int[] lineLength = new int[fold.Count];
        var total = 0;
        for (var i = 0; i < fold.Count; i++)
        {
            lineStart[i] = total;
            lineLength[i] = fold[i].Length;
            total += fold[i].Length + 1; // content + unique terminator
        }

        var s = new int[total];
        var pos = 0;
        for (var i = 0; i < fold.Count; i++)
        {
            foreach (var ch in fold[i]) s[pos++] = ch;
            s[pos++] = terminatorBase + i; // unique terminator per line
        }

        var n = total;
        var sa = new int[n];
        var rank = new int[n];
        var tmp = new int[n];
        for (var i = 0; i < n; i++)
        {
            sa[i] = i;
            rank[i] = s[i];
        }

        var k = 1;
        while (k < n)
        {
            var key = k;
            Array.Sort(sa, Comparer<int>.Create((int a, int b) =>
            {
                if (rank[a] != rank[b]) return rank[a].CompareTo(rank[b]);
                var ra = a + key < n ? rank[a + key] : -1;
                var rb = b + key < n ? rank[b + key] : -1;
                return ra.CompareTo(rb);
            }));
            tmp[sa[0]] = 0;
            for (var i = 1; i < n; i++)
            {
                var a = sa[i - 1];
                var b = sa[i];
                var greater = rank[a] != rank[b]
                    ? rank[a] < rank[b]
                    : (a + k < n ? rank[a + k] : -1) < (b + k < n ? rank[b + k] : -1);
                tmp[b] = tmp[a] + (greater ? 1 : 0);
            }
            Array.Copy(tmp, rank, n);
            if (rank[sa[n - 1]] == n - 1) break;
            k <<= 1;
        }

        // Kasai LCP. lcp[r] = LCP(sa[r-1], sa[r]) for r >= 1.
        var lcp = new int[n];
        var h = 0;
        for (var i = 0; i < n; i++)
        {
            var r = rank[i];
            if (r == 0) { h = 0; continue; }
            var j = sa[r - 1];
            while (i + h < n && j + h < n && s[i + h] == s[j + h]) h++;
            lcp[r] = h;
            if (h > 0) h--;
        }

        // Which line each suffix belongs to. Compute line-of-position once,
        // then map via the suffix array (which is NOT position-sorted).
        var lineOfPosition = new int[total];
        {
            var l = 0;
            for (var p = 0; p < total; p++)
            {
                while (l + 1 < fold.Count && p >= lineStart[l + 1]) l++;
                lineOfPosition[p] = l;
            }
        }
        var lineOfRank = new int[n];
        for (var r = 0; r < n; r++) lineOfRank[r] = lineOfPosition[sa[r]];

        // Nearest rank with a different line to the left / right.
        var prevDiff = new int[n];
        var nextDiff = new int[n];
        var last = -1;
        for (var r = 0; r < n; r++)
        {
            prevDiff[r] = last;
            if (r + 1 < n && lineOfRank[r] != lineOfRank[r + 1]) last = r;
        }
        last = -1;
        for (var r = n - 1; r >= 0; r--)
        {
            nextDiff[r] = last;
            if (r > 0 && lineOfRank[r] != lineOfRank[r - 1]) last = r;
        }

        // RMQ over lcp[1..n-1] (index 0 unused).
        var rmq = SparseTable.Build(lcp);

        return new SuffixIndex(s, sa, rank, lcp, lineOfRank, prevDiff, nextDiff, rmq, lineStart, lineLength, lines.ToArray(), caseInsensitive);
    }

    private static IReadOnlyList<string> FoldLines(IReadOnlyList<string> lines)
    {
        var folded = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var src = lines[i];
            var chars = new char[src.Length];
            for (var j = 0; j < src.Length; j++) chars[j] = char.ToLowerInvariant(src[j]);
            folded[i] = new string(chars);
        }
        return folded;
    }

    private int LcpBetweenRanks(int a, int b)
    {
        if (a == b) return int.MaxValue;
        if (a > b) (a, b) = (b, a);
        // min lcp[a+1..b]
        var lo = a + 1;
        var hi = b; // inclusive
        var len = hi - lo + 1;
        var k = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)len);
        return Math.Min(_rmq.Query(lo, k), _rmq.Query(hi - (1 << k) + 1, k));
    }

    /// <summary>
    /// Returns the shortest substring of the given line that does not appear in any
    /// other line of the index. Prefers (by length, then by not containing the digit
    /// placeholder) candidates that avoid the value token. Returns null if the line
    /// is identical to another line and has no unique substring.
    /// </summary>
    public string? ShortestUniqueSubstring(int lineIndex) =>
        ShortestUniqueSubstring(lineIndex, avoidPlaceholder: false);

    /// <summary>
    /// Shortest unique substring of the given line, restricted to candidates that
    /// avoid the digit placeholder when <paramref name="avoidPlaceholder"/> is true.
    /// The placeholder-avoiding form may return a longer substring; it returns null
    /// when no placeholder-free unique substring exists.
    /// </summary>
    public string? ShortestUniqueSubstring(int lineIndex, bool avoidPlaceholder)
    {
        var start = _lineStart[lineIndex];
        var len = _lineLength[lineIndex];
        if (len == 0) return null;

        var bestLen = len + 1;
        var bestStart = -1;
        var bestHasPlaceholder = false;

        for (var offset = 0; offset < len; offset++)
        {
            var r = _rank[start + offset];

            var other = 0;
            if (_prevDiff[r] != -1)
            {
                var left = LcpBetweenRanks(_prevDiff[r], r);
                if (left > other) other = left;
            }
            if (_nextDiff[r] != -1)
            {
                var right = LcpBetweenRanks(r, _nextDiff[r]);
                if (right > other) other = right;
            }

            var uniqueLen = other + 1;
            // If the unique fragment would extend past the end of the line, the
            // entire remaining tail is shared with another line, so this offset
            // offers no unique substring at all.
            if (uniqueLen > len - offset) continue;
            if (avoidPlaceholder && HasPlaceholder(start + offset, uniqueLen)) continue;
            if (uniqueLen >= bestLen)
            {
                if (uniqueLen == bestLen)
                {
                    var hasPlaceholder = HasPlaceholder(start + offset, uniqueLen);
                    if (hasPlaceholder && !bestHasPlaceholder)
                    {
                        // keep the earlier placeholder-free candidate
                        continue;
                    }
                    if (!hasPlaceholder && bestHasPlaceholder)
                    {
                        bestStart = start + offset;
                        bestLen = uniqueLen;
                        bestHasPlaceholder = false;
                    }
                }
                continue;
            }

            bestStart = start + offset;
            bestLen = uniqueLen;
            bestHasPlaceholder = HasPlaceholder(start + offset, uniqueLen);
        }

        if (bestStart < 0 || bestLen > len) return null;
        return _corpus[lineIndex].Substring(bestStart - _lineStart[lineIndex], bestLen);
    }

    private bool HasPlaceholder(int pos, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (_s[pos + i] == DigitPlaceholder) return true;
        }
        return false;
    }

    /// <summary>
    /// True if the given fragment (as a prefix of some suffix) occurs only within the
    /// given line, i.e. matches no other line in the index.
    /// </summary>
    public bool FragmentUniqueToLine(int lineIndex, string fragment)
    {
        if (fragment.Length == 0) return false;
        var lo = LowerBound(fragment);
        var hi = UpperBound(fragment);
        if (lo >= hi) return false;
        for (var r = lo; r < hi; r++)
        {
            if (_lineOfRank[r] != lineIndex) return false;
        }
        return true;
    }

    /// <summary>
    /// The distinct line indexes whose content contains the fragment as a substring.
    /// </summary>
    public int[] LinesMatching(string fragment)
    {
        if (fragment.Length == 0) return [];
        var lo = LowerBound(fragment);
        var hi = UpperBound(fragment);
        if (lo >= hi) return [];
        var seen = new System.Collections.Generic.HashSet<int>();
        for (var r = lo; r < hi; r++) seen.Add(_lineOfRank[r]);
        var result = new int[seen.Count];
        seen.CopyTo(result);
        Array.Sort(result);
        return result;
    }

    private int LowerBound(string fragment)
    {
        var lo = 0;
        var hi = _n;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (CompareWithSuffix(_sa[mid], fragment) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private int UpperBound(string fragment)
    {
        var lo = 0;
        var hi = _n;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (CompareWithSuffix(_sa[mid], fragment) <= 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private int CompareWithSuffix(int suffixStart, string fragment)
    {
        for (var i = 0; i < fragment.Length; i++)
        {
            var p = suffixStart + i;
            if (p >= _n) return -1; // suffix exhausted before fragment
            var sc = _s[p];
            if (sc == '\0') continue; // terminator region: treat as end
            var fc = _caseInsensitive ? char.ToLowerInvariant(fragment[i]) : fragment[i];
            if (sc != fc) return sc.CompareTo(fc);
        }
        return 0;
    }

    private sealed class SparseTable
    {
        private readonly int[][] _st;

        private SparseTable(int[][] st) => _st = st;

        public static SparseTable Build(int[] values)
        {
            var n = values.Length;
            var kMax = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(1, n));
            var st = new int[kMax][];
            st[0] = (int[])values.Clone();
            for (var k = 1; k < kMax; k++)
            {
                var prev = st[k - 1];
                var cur = new int[n - (1 << k) + 1];
                var step = 1 << (k - 1);
                for (var i = 0; i < cur.Length; i++)
                {
                    cur[i] = Math.Min(prev[i], prev[i + step]);
                }
                st[k] = cur;
            }
            return new SparseTable(st);
        }

        /// <summary>Range-min over values[lo..lo+2^k-1].</summary>
        public int Query(int lo, int k) => _st[k][lo];
    }
}
