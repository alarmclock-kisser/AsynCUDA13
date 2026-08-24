// Atomically adds a float to a global accumulator using an integer compare-and-swap loop.
// (OpenCL 1.2 has no native float atomics; this mirrors the CUDA atomicCAS/__int_as_float idiom.)
inline void atomic_add_float(__global float* address, float value)
{
	__global volatile int* intAddress = (__global volatile int*)address;
	int old = *intAddress;
	int assumed;
	do
	{
		assumed = old;
		float updated = as_float(assumed) + value;
		old = atomic_cmpxchg(intAddress, assumed, as_int(updated));
	} while (assumed != old);
}

// Sums masked float values into a float float result.
// Launched with global size = length (one work-item per row).
__kernel void sum_float_by_mask(
	__global const float* values,
	__global const uchar* mask,
	__global float* result,
	int length)
{
	int idx = get_global_id(0);
	if (idx < length && mask[idx] != 0)
	{
		atomic_add_float(result, values[idx]);
	}
}
