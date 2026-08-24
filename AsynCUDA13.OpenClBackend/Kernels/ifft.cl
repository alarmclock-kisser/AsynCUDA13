typedef struct {
	float x;
	float y;
} Vector2;

#define M_PI 3.14159265358979323846f

// Full-length inverse FFT over a float, power-of-two sized complex buffer.
// In-place radix-2 Cooley-Tukey with positive sign. NO normalization.
// Scaling by 1/n is performed by the separate normalize kernel.
// 'n' must be a power of two; the host pads the data accordingly.
// Launched with a float work-item (global size 1).
__kernel void ifft_full(
	__global Vector2* data,
	int n)
{
	if (get_global_id(0) != 0) {
		return;
	}

	// 1. IFFT butterfly (positive sign)
	for (int s = 1; s < n; s <<= 1) {
		int m = s << 1;
		float theta = 2.0f * M_PI / (float)m;

		for (int k = 0; k < n; k += m) {
			for (int j = 0; j < s; j++) {
				int idx1 = k + j;
				int idx2 = idx1 + s;

				Vector2 u = data[idx1];
				Vector2 v = data[idx2];

				float c = cos(j * theta);
				float sn = sin(j * theta);
				float t_real = c * v.x - sn * v.y;
				float t_imag = sn * v.x + c * v.y;

				data[idx1].x = u.x + t_real;
				data[idx1].y = u.y + t_imag;

				data[idx2].x = u.x - t_real;
				data[idx2].y = u.y - t_imag;
			}
		}
	}

	// 2. Bit reversal
	for (int i = 0; i < n; i++) {
		int j = 0, nn = i;
		for (int b = n >> 1; b > 0; b >>= 1) {
			j = (j << 1) | (nn & 1);
			nn >>= 1;
		}
		if (j > i) {
			Vector2 tmp = data[i];
			data[i] = data[j];
			data[j] = tmp;
		}
	}
}
