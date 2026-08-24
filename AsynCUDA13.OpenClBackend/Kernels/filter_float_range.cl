// Filters a float column for an inclusive range, writing a 0/1 byte mask.
// Launched with global size = length (one work-item per row).
__kernel void filter_float_range(
	__global const float* column,
	__global uchar* mask,
	int length,
	float minValue,
	float maxValue)
{
	int idx = get_global_id(0);
	if (idx < length)
	{
		float v = column[idx];
		mask[idx] = (v >= minValue && v <= maxValue) ? (uchar)1 : (uchar)0;
	}
}
