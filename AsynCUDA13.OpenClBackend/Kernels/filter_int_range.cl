// Filters an int column for an inclusive range, writing a 0/1 byte mask.
// Launched with global size = length (one work-item per row).
__kernel void filter_int_range(
	__global const int* column,
	__global uchar* mask,
	int length,
	int minValue,
	int maxValue)
{
	int idx = get_global_id(0);
	if (idx < length)
	{
		int v = column[idx];
		mask[idx] = (v >= minValue && v <= maxValue) ? (uchar)1 : (uchar)0;
	}
}
