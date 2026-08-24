typedef struct {
	float x;
	float y;
} Vector2;

// Scales every complex element by 'scale' in-place.
// Used to normalize an IFFT result by 1/n.
// Launched with global size n (one work-item per element).
__kernel void normalize_complex(
	__global Vector2* data,
	float scale,
	int n)
{
	int i = get_global_id(0);
	if (i >= n) {
		return;
	}

	data[i].x *= scale;
	data[i].y *= scale;
}
