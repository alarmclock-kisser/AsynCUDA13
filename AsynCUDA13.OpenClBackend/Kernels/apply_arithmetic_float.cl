// Applies a simple arithmetic transform to a float column in place.
// op: 0 = add, 1 = subtract, 2 = multiply, 3 = divide (division by zero is a no-op).
// Launched with global size = length (one work-item per element).
__kernel void apply_arithmetic_float(
	__global float* data,
	int length,
	int op,
	float operand)
{
	int idx = get_global_id(0);
	if (idx >= length)
	{
		return;
	}

	float v = data[idx];

	if (op == 0)
	{
		v = v + operand;
	}
	else if (op == 1)
	{
		v = v - operand;
	}
	else if (op == 2)
	{
		v = v * operand;
	}
	else if (op == 3)
	{
		v = (operand != 0.0f) ? (v / operand) : v;
	}

	data[idx] = v;
}
