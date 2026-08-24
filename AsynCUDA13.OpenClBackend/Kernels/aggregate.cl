typedef struct {
	float x;
	float y;
} Vector2;

// Extracts the real part of a complex buffer into a real output buffer.
// Used to finalize an IFFT result (drops the imaginary part).
// Launched with global size n (one work-item per output element).
__kernel void extract_real(
	__global const Vector2* input,
	__global float* output,
	int n)
{
	int i = get_global_id(0);
	if (i >= n) {
		return;
	}

	output[i] = input[i].x;
}
