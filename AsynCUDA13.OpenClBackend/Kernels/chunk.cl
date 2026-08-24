typedef struct {
	float x;
	float y;
} Vector2;

// Prepares a real input array for a full-length complex FFT.
// Copies srcLength real samples into the real part of a padded complex buffer
// of size n (power of two); indices >= srcLength are zero-filled.
// Launched with global size n (one work-item per output element).
__kernel void pad_real_to_complex(
	__global const float* input,
	__global Vector2* output,
	int srcLength,
	int n)
{
	int i = get_global_id(0);
	if (i >= n) {
		return;
	}

	if (i < srcLength) {
		output[i].x = input[i];
	} else {
		output[i].x = 0.0f;
	}
	output[i].y = 0.0f;
}
