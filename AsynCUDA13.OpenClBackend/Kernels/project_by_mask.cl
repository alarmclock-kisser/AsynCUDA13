// Copies masked float values into an output buffer (0 where unmasked).
// Launched with global size = length (one work-item per row).
__kernel void project_by_mask(
	__global const float* input,
	__global const uchar* mask,
	__global float* output,
	int length)
{
	int idx = get_global_id(0);
	if (idx < length)
	{
		output[idx] = (mask[idx] != 0) ? input[idx] : 0.0f;
	}
}
