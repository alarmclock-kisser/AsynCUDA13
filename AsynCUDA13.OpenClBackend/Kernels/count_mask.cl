// Counts the set entries of a byte mask into a float int result.
// Launched with global size = length (one work-item per row).
__kernel void count_mask(
	__global const uchar* mask,
	__global int* result,
	int length)
{
	int idx = get_global_id(0);
	if (idx < length && mask[idx] != 0)
	{
		atomic_add(result, 1);
	}
}
