// Marks rows whose UTF-8 string is within a bounded Levenshtein distance of a pattern.
// Both string and pattern are clamped to MAXLEN bytes to bound per-work-item memory.
// Launched with global size = rowCount (one work-item per row).
__kernel void string_fuzzy_levenshtein_limited(
	__global const uchar* data,
	__global const int* offsets,
	__global const int* lengths,
	__global const uchar* pattern,
	__global uchar* mask,
	int rowCount,
	int patternLength,
	int maxDistance)
{
	int row = get_global_id(0);
	if (row >= rowCount)
	{
		return;
	}

	const int MAXLEN = 64;

	int plen = patternLength;
	if (plen > MAXLEN)
	{
		plen = MAXLEN;
	}

	int slen = lengths[row];
	if (slen > MAXLEN)
	{
		slen = MAXLEN;
	}

	int start = offsets[row];

	int prev[MAXLEN + 1];
	int curr[MAXLEN + 1];

	for (int j = 0; j <= plen; j++)
	{
		prev[j] = j;
	}

	for (int i = 1; i <= slen; i++)
	{
		curr[0] = i;
		uchar sc = data[start + i - 1];
		for (int j = 1; j <= plen; j++)
		{
			int cost = (sc == pattern[j - 1]) ? 0 : 1;
			int del = prev[j] + 1;
			int ins = curr[j - 1] + 1;
			int sub = prev[j - 1] + cost;
			int best = del < ins ? del : ins;
			best = best < sub ? best : sub;
			curr[j] = best;
		}
		for (int j = 0; j <= plen; j++)
		{
			prev[j] = curr[j];
		}
	}

	mask[row] = (prev[plen] <= maxDistance) ? (uchar)1 : (uchar)0;
}
