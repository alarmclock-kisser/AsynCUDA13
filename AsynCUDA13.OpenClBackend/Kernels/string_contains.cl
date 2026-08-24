// Marks rows whose UTF-8 string contains a byte pattern.
// strings are stored as a contiguous byte buffer plus per-row offsets and lengths.
// Launched with global size = rowCount (one work-item per row).
__kernel void string_contains(
	__global const uchar* data,
	__global const int* offsets,
	__global const int* lengths,
	__global const uchar* pattern,
	__global uchar* mask,
	int rowCount,
	int patternLength)
{
	int row = get_global_id(0);
	if (row >= rowCount)
	{
		return;
	}

	int start = offsets[row];
	int len = lengths[row];
	uchar found = 0;

	if (patternLength > 0 && len >= patternLength)
	{
		for (int i = 0; i <= len - patternLength; i++)
		{
			int j = 0;
			while (j < patternLength && data[start + i + j] == pattern[j])
			{
				j++;
			}
			if (j == patternLength)
			{
				found = 1;
				break;
			}
		}
	}

	mask[row] = found;
}
