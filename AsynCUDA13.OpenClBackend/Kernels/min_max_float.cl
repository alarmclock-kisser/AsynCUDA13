// Atomic float min/max using an integer compare-and-swap loop.
// (OpenCL 1.2 has no native float atomics; this mirrors the CUDA atomicCAS/__int_as_float idiom.)
inline void atomic_min_float(__global float* address, float value)
{
	__global volatile int* intAddress = (__global volatile int*)address;
	int old = *intAddress;
	while (value < as_float(old))
	{
		int assumed = old;
		old = atomic_cmpxchg(intAddress, assumed, as_int(value));
		if (old == assumed)
		{
			break;
		}
	}
}

inline void atomic_max_float(__global float* address, float value)
{
	__global volatile int* intAddress = (__global volatile int*)address;
	int old = *intAddress;
	while (value > as_float(old))
	{
		int assumed = old;
		old = atomic_cmpxchg(intAddress, assumed, as_int(value));
		if (old == assumed)
		{
			break;
		}
	}
}

// Computes the minimum and maximum of a float column.
// Launched with global size = length (one work-item per element).
__kernel void min_max_float(
	__global const float* values,
	__global float* outMin,
	__global float* outMax,
	int length)
{
	int idx = get_global_id(0);
	if (idx < length)
	{
		float v = values[idx];
		atomic_min_float(outMin, v);
		atomic_max_float(outMax, v);
	}
}
