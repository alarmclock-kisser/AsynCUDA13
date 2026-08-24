// Filters an int column for equality with a value, writing a 0/1 byte mask.
// Launched with global size = length (one work-item per row).
__kernel void filter_int_equals(
	__global const int* column,
	__global uchar* mask,
	int length,
	int value)
{
	int idx = get_global_id(0);
	if (idx < length)
	{
		mask[idx] = (column[idx] == value) ? (uchar)1 : (uchar)0;
	}
}
