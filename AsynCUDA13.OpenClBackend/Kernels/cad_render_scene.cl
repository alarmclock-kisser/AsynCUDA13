#define CAD_INFINITY 1.0e20f
#define CAD_MAX_PRISM_POINTS 128

typedef struct
{
	float kind;
	float positionX;
	float positionY;
	float positionZ;
	float scaleX;
	float scaleY;
	float scaleZ;
	float pointsOffset;
	float pointsCount;
	float selected;
} CadPrimitive;

inline float3 cad_vec(float x, float y, float z)
{
	return (float3)(x, y, z);
}

inline float cad_dot(float3 a, float3 b)
{
	return a.x * b.x + a.y * b.y + a.z * b.z;
}

inline float3 cad_cross(float3 a, float3 b)
{
	return cad_vec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}

inline float3 cad_normalize(float3 value)
{
	float length = sqrt(cad_dot(value, value));
	return length > 1.0e-12f ? cad_vec(value.x / length, value.y / length, value.z / length) : cad_vec(0.0f, 1.0f, 0.0f);
}

inline float cad_triangle_hit(float3 origin, float3 direction, float3 a, float3 b, float3 c, __private float3* normal)
{
	float3 edge1 = cad_vec(b.x - a.x, b.y - a.y, b.z - a.z);
	float3 edge2 = cad_vec(c.x - a.x, c.y - a.y, c.z - a.z);
	float3 p = cad_cross(direction, edge2);
	float determinant = cad_dot(edge1, p);
	if (fabs(determinant) < 1.0e-7f)
	{
		return CAD_INFINITY;
	}

	float inverse = 1.0f / determinant;
	float3 offset = cad_vec(origin.x - a.x, origin.y - a.y, origin.z - a.z);
	float u = cad_dot(offset, p) * inverse;
	if (u < 0.0f || u > 1.0f)
	{
		return CAD_INFINITY;
	}

	float3 q = cad_cross(offset, edge1);
	float v = cad_dot(direction, q) * inverse;
	if (v < 0.0f || u + v > 1.0f)
	{
		return CAD_INFINITY;
	}

	float distance = cad_dot(edge2, q) * inverse;
	if (distance <= 0.001f)
	{
		return CAD_INFINITY;
	}

	*normal = cad_normalize(cad_cross(edge1, edge2));
	return distance;
}

inline void cad_consider_triangle(float3 origin, float3 direction, float3 a, float3 b, float3 c, __private float* nearest, __private float3* normal)
{
	float3 candidateNormal;
	float distance = cad_triangle_hit(origin, direction, a, b, c, &candidateNormal);
	if (distance < *nearest)
	{
		*nearest = distance;
		*normal = candidateNormal;
	}
}

inline float cad_box_hit(float3 origin, float3 direction, __private float3* normal)
{
	float originValues[3] = { origin.x, origin.y, origin.z };
	float directionValues[3] = { direction.x, direction.y, direction.z };
	float nearDistance = -CAD_INFINITY;
	float farDistance = CAD_INFINITY;
	float nearNormal[3] = { 0.0f, 0.0f, 0.0f };
	float farNormal[3] = { 0.0f, 0.0f, 0.0f };

	for (int axis = 0; axis < 3; axis++)
	{
		if (fabs(directionValues[axis]) < 1.0e-8f)
		{
			if (originValues[axis] < -0.5f || originValues[axis] > 0.5f)
			{
				return CAD_INFINITY;
			}
			continue;
		}

		float first = (-0.5f - originValues[axis]) / directionValues[axis];
		float second = (0.5f - originValues[axis]) / directionValues[axis];
		float firstSign = -1.0f;
		float secondSign = 1.0f;
		if (first > second)
		{
			float swap = first;
			first = second;
			second = swap;
			firstSign = 1.0f;
			secondSign = -1.0f;
		}
		if (first > nearDistance)
		{
			nearDistance = first;
			nearNormal[0] = nearNormal[1] = nearNormal[2] = 0.0f;
			nearNormal[axis] = firstSign;
		}
		if (second < farDistance)
		{
			farDistance = second;
			farNormal[0] = farNormal[1] = farNormal[2] = 0.0f;
			farNormal[axis] = secondSign;
		}
		if (nearDistance > farDistance)
		{
			return CAD_INFINITY;
		}
	}

	if (farDistance <= 0.001f)
	{
		return CAD_INFINITY;
	}
	if (nearDistance > 0.001f)
	{
		*normal = cad_vec(nearNormal[0], nearNormal[1], nearNormal[2]);
		return nearDistance;
	}
	*normal = cad_vec(farNormal[0], farNormal[1], farNormal[2]);
	return farDistance;
}

inline float cad_sphere_hit(float3 origin, float3 direction, float radius, __private float3* normal)
{
	float a = cad_dot(direction, direction);
	float halfB = cad_dot(origin, direction);
	float c = cad_dot(origin, origin) - radius * radius;
	float discriminant = halfB * halfB - a * c;
	if (discriminant < 0.0f)
	{
		return CAD_INFINITY;
	}

	float root = sqrt(discriminant);
	float distance = (-halfB - root) / a;
	if (distance <= 0.001f)
	{
		distance = (-halfB + root) / a;
	}
	if (distance <= 0.001f)
	{
		return CAD_INFINITY;
	}

	*normal = cad_normalize(cad_vec(origin.x + direction.x * distance, origin.y + direction.y * distance, origin.z + direction.z * distance));
	return distance;
}

inline float cad_inside_prism(float x, float z, __global const float* sceneData, int offset, int count)
{
	int inside = 0;
	for (int i = 0, j = count - 1; i < count; j = i++)
	{
		float xi = sceneData[offset + i * 2];
		float zi = sceneData[offset + i * 2 + 1];
		float xj = sceneData[offset + j * 2];
		float zj = sceneData[offset + j * 2 + 1];
		if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi)
		{
			inside = !inside;
		}
	}
	return inside ? 1.0f : 0.0f;
}

inline float cad_prism_hit(CadPrimitive primitive, __global const float* sceneData, float3 origin, float3 direction, __private float3* normal)
{
	int count = (int) primitive.pointsCount;
	int offset = (int) primitive.pointsOffset;
	if (count < 3 || count > CAD_MAX_PRISM_POINTS)
	{
		return CAD_INFINITY;
	}

	float nearest = CAD_INFINITY;
	float3 nearestNormal = cad_vec(0.0f, 1.0f, 0.0f);
	for (int i = 0; i < count; i++)
	{
		int next = (i + 1) % count;
		float x0 = sceneData[offset + i * 2];
		float z0 = sceneData[offset + i * 2 + 1];
		float x1 = sceneData[offset + next * 2];
		float z1 = sceneData[offset + next * 2 + 1];
		float3 bottom0 = cad_vec(x0, 0.0f, z0);
		float3 bottom1 = cad_vec(x1, 0.0f, z1);
		float3 top0 = cad_vec(x0, 1.0f, z0);
		float3 top1 = cad_vec(x1, 1.0f, z1);
		cad_consider_triangle(origin, direction, bottom0, bottom1, top1, &nearest, &nearestNormal);
		cad_consider_triangle(origin, direction, bottom0, top1, top0, &nearest, &nearestNormal);
	}

	if (fabs(direction.y) > 1.0e-8f)
	{
		for (int cap = 0; cap < 2; cap++)
		{
			float y = (float) cap;
			float distance = (y - origin.y) / direction.y;
			float x = origin.x + direction.x * distance;
			float z = origin.z + direction.z * distance;
			if (distance > 0.001f && distance < nearest && cad_inside_prism(x, z, sceneData, offset, count) > 0.5f)
			{
				nearest = distance;
				nearestNormal = cad_vec(0.0f, cap == 0 ? -1.0f : 1.0f, 0.0f);
			}
		}
	}

	*normal = nearestNormal;
	return nearest;
}

inline float cad_line_hit(CadPrimitive primitive, __global const float* sceneData, float3 eye, float3 direction, float focalLength)
{
	if ((int) primitive.pointsCount != 2)
	{
		return CAD_INFINITY;
	}

	int offset = (int) primitive.pointsOffset;
	float3 start = cad_vec(sceneData[offset], sceneData[offset + 1], sceneData[offset + 2]);
	float3 end = cad_vec(sceneData[offset + 3], sceneData[offset + 4], sceneData[offset + 5]);
	float3 segment = cad_vec(end.x - start.x, end.y - start.y, end.z - start.z);
	float3 relative = cad_vec(eye.x - start.x, eye.y - start.y, eye.z - start.z);
	float a = cad_dot(direction, direction);
	float b = cad_dot(direction, segment);
	float c = cad_dot(segment, segment);
	float d = cad_dot(direction, relative);
	float e = cad_dot(segment, relative);
	float denominator = a * c - b * b;
	float u = denominator > 1.0e-8f ? (a * e - b * d) / denominator : 0.0f;
	u = clamp(u, 0.0f, 1.0f);
	float3 point = cad_vec(start.x + u * segment.x, start.y + u * segment.y, start.z + u * segment.z);
	float distance = cad_dot(cad_vec(point.x - eye.x, point.y - eye.y, point.z - eye.z), direction);
	if (distance <= 0.001f)
	{
		return CAD_INFINITY;
	}

	float3 rayPoint = cad_vec(eye.x + direction.x * distance, eye.y + direction.y * distance, eye.z + direction.z * distance);
	float3 difference = cad_vec(rayPoint.x - point.x, rayPoint.y - point.y, rayPoint.z - point.z);
	return cad_dot(difference, difference) <= fmax(0.0004f, distance * distance * 2.25f / (focalLength * focalLength)) ? distance : CAD_INFINITY;
}

inline float cad_intersect_primitive(CadPrimitive primitive, __global const float* sceneData, float3 eye, float3 direction, float focalLength, __private float3* worldNormal)
{
	if ((int) primitive.kind == 6)
	{
		*worldNormal = cad_vec(-direction.x, -direction.y, -direction.z);
		return cad_line_hit(primitive, sceneData, eye, direction, focalLength);
	}

	if (fabs(primitive.scaleX) < 1.0e-6f || fabs(primitive.scaleY) < 1.0e-6f || fabs(primitive.scaleZ) < 1.0e-6f)
	{
		return CAD_INFINITY;
	}

	float3 position = cad_vec(primitive.positionX, primitive.positionY, primitive.positionZ);
	float3 scale = cad_vec(primitive.scaleX, primitive.scaleY, primitive.scaleZ);
	float3 origin = cad_vec((eye.x - position.x) / scale.x, (eye.y - position.y) / scale.y, (eye.z - position.z) / scale.z);
	float3 localDirection = cad_vec(direction.x / scale.x, direction.y / scale.y, direction.z / scale.z);
	float3 localNormal = cad_vec(0.0f, 1.0f, 0.0f);
	float distance = CAD_INFINITY;

	switch ((int) primitive.kind)
	{
		case 0:
			distance = cad_box_hit(origin, localDirection, &localNormal);
			break;
		case 1:
		case 5:
			distance = cad_sphere_hit(origin, localDirection, 0.5f, &localNormal);
			break;
		case 2:
		{
			float nearest = CAD_INFINITY;
			float3 nearestNormal = cad_vec(0.0f, 1.0f, 0.0f);
			float3 v0 = cad_vec(-0.5f, 0.0f, -0.5f);
			float3 v1 = cad_vec(0.5f, 0.0f, -0.5f);
			float3 v2 = cad_vec(0.5f, 0.0f, 0.5f);
			float3 v3 = cad_vec(-0.5f, 0.0f, 0.5f);
			float3 apex = cad_vec(0.0f, 1.0f, 0.0f);
			cad_consider_triangle(origin, localDirection, v0, v2, v1, &nearest, &nearestNormal);
			cad_consider_triangle(origin, localDirection, v0, v3, v2, &nearest, &nearestNormal);
			cad_consider_triangle(origin, localDirection, v0, v1, apex, &nearest, &nearestNormal);
			cad_consider_triangle(origin, localDirection, v1, v2, apex, &nearest, &nearestNormal);
			cad_consider_triangle(origin, localDirection, v2, v3, apex, &nearest, &nearestNormal);
			cad_consider_triangle(origin, localDirection, v3, v0, apex, &nearest, &nearestNormal);
			distance = nearest;
			localNormal = nearestNormal;
			break;
		}
		case 3:
			distance = cad_prism_hit(primitive, sceneData, origin, localDirection, &localNormal);
			break;
		case 4:
		{
			float nearest = CAD_INFINITY;
			float3 nearestNormal = cad_vec(0.0f, 1.0f, 0.0f);
			float a = localDirection.x * localDirection.x + localDirection.z * localDirection.z;
			if (a > 1.0e-8f)
			{
				float halfB = origin.x * localDirection.x + origin.z * localDirection.z;
				float c = origin.x * origin.x + origin.z * origin.z - 0.25f;
				float discriminant = halfB * halfB - a * c;
				if (discriminant >= 0.0f)
				{
					float root = sqrt(discriminant);
					for (int side = 0; side < 2; side++)
					{
						float t = (-halfB + (side == 0 ? -root : root)) / a;
						float y = origin.y + localDirection.y * t;
						if (t > 0.001f && y >= -0.5f && y <= 0.5f && t < nearest)
						{
							nearest = t;
							nearestNormal = cad_normalize(cad_vec(origin.x + localDirection.x * t, 0.0f, origin.z + localDirection.z * t));
						}
					}
				}
			}
			if (fabs(localDirection.y) > 1.0e-8f)
			{
				for (int cap = 0; cap < 2; cap++)
				{
					float y = cap == 0 ? -0.5f : 0.5f;
					float t = (y - origin.y) / localDirection.y;
					float x = origin.x + localDirection.x * t;
					float z = origin.z + localDirection.z * t;
					if (t > 0.001f && x * x + z * z <= 0.25f && t < nearest)
					{
						nearest = t;
						nearestNormal = cad_vec(0.0f, cap == 0 ? -1.0f : 1.0f, 0.0f);
					}
				}
			}
			distance = nearest;
			localNormal = nearestNormal;
			break;
		}
		default:
			return CAD_INFINITY;
	}

	if (distance < CAD_INFINITY)
	{
		*worldNormal = cad_normalize(cad_vec(localNormal.x / scale.x, localNormal.y / scale.y, localNormal.z / scale.z));
	}
	return distance;
}

inline float3 cad_material(CadPrimitive primitive)
{
	if (primitive.selected > 0.5f)
	{
		return cad_vec(0.20f, 0.88f, 0.58f);
	}
	switch ((int) primitive.kind)
	{
		case 0: return cad_vec(0.28f, 0.67f, 0.88f);
		case 1: return cad_vec(0.63f, 0.48f, 0.88f);
		case 2: return cad_vec(0.94f, 0.65f, 0.30f);
		case 3: return cad_vec(0.30f, 0.76f, 0.59f);
		case 4: return cad_vec(0.84f, 0.48f, 0.43f);
		default: return cad_vec(0.96f, 0.82f, 0.25f);
	}
}

inline float cad_channel(float value)
{
	return clamp(value * 255.0f + 0.5f, 0.0f, 255.0f);
}

__kernel void cad_render_scene(
	__global const float* sceneData,
	int primitiveCount,
	__global float* outputPixels,
	int width,
	int height,
	float eyeX,
	float eyeY,
	float eyeZ,
	float rightX,
	float rightY,
	float rightZ,
	float upX,
	float upY,
	float upZ,
	float forwardX,
	float forwardY,
	float forwardZ,
	float focalLength)
{
	int pixel = get_global_id(0);
	int pixelCount = width * height;
	if (pixel >= pixelCount)
	{
		return;
	}
	int x = pixel % width;
	int y = pixel / width;

	float3 eye = cad_vec(eyeX, eyeY, eyeZ);
	float3 right = cad_vec(rightX, rightY, rightZ);
	float3 up = cad_vec(upX, upY, upZ);
	float3 forward = cad_vec(forwardX, forwardY, forwardZ);
	float cameraX = ((float) x + 0.5f - (float) width * 0.5f) / focalLength;
	float cameraY = ((float) height * 0.5f - (float) y - 0.5f) / focalLength;
	float3 direction = cad_normalize(cad_vec(
		forward.x + cameraX * right.x + cameraY * up.x,
		forward.y + cameraX * right.y + cameraY * up.y,
		forward.z + cameraX * right.z + cameraY * up.z));

	float gradient = 1.0f - (float) y / (float) height;
	float3 color = cad_vec(0.055f + 0.025f * gradient, 0.085f + 0.025f * gradient, 0.105f + 0.025f * gradient);
	float nearest = CAD_INFINITY;
	float3 nearestNormal = cad_vec(0.0f, 1.0f, 0.0f);
	CadPrimitive nearestPrimitive;
	nearestPrimitive.kind = -1.0f;

	for (int index = 0; index < primitiveCount; index++)
	{
		int base = index * 10;
		CadPrimitive primitive;
		primitive.kind = sceneData[base];
		primitive.positionX = sceneData[base + 1];
		primitive.positionY = sceneData[base + 2];
		primitive.positionZ = sceneData[base + 3];
		primitive.scaleX = sceneData[base + 4];
		primitive.scaleY = sceneData[base + 5];
		primitive.scaleZ = sceneData[base + 6];
		primitive.pointsOffset = sceneData[base + 7];
		primitive.pointsCount = sceneData[base + 8];
		primitive.selected = sceneData[base + 9];
		float3 normal;
		float distance = cad_intersect_primitive(primitive, sceneData, eye, direction, focalLength, &normal);
		if (distance < nearest)
		{
			nearest = distance;
			nearestNormal = normal;
			nearestPrimitive = primitive;
		}
	}

	if (fabs(direction.y) > 1.0e-8f)
	{
		float groundDistance = -eye.y / direction.y;
		if (groundDistance > 0.001f && groundDistance < nearest)
		{
			float3 ground = cad_vec(eye.x + direction.x * groundDistance, 0.0f, eye.z + direction.z * groundDistance);
			float lineWidth = fmax(0.006f, groundDistance * 1.5f / focalLength);
			float nearestX = fabs(ground.x - round(ground.x));
			float nearestZ = fabs(ground.z - round(ground.z));
			float fade = fmax(0.0f, 1.0f - groundDistance / 80.0f);
			color = cad_vec(0.055f + 0.035f * fade, 0.075f + 0.045f * fade, 0.09f + 0.05f * fade);
			if (nearestX < lineWidth || nearestZ < lineWidth)
			{
				color = cad_vec(0.15f, 0.20f, 0.23f);
				if (fabs(ground.z) < lineWidth * 1.3f) color = cad_vec(0.43f, 0.16f, 0.17f);
				if (fabs(ground.x) < lineWidth * 1.3f) color = cad_vec(0.14f, 0.36f, 0.39f);
				if (fabs(ground.z) > lineWidth * 1.3f && fabs(round(ground.x) - ground.x) < lineWidth && fmod(fabs(round(ground.x)), 5.0f) < 0.01f) color = cad_vec(0.22f, 0.28f, 0.31f);
				if (fabs(ground.x) > lineWidth * 1.3f && fabs(round(ground.z) - ground.z) < lineWidth && fmod(fabs(round(ground.z)), 5.0f) < 0.01f) color = cad_vec(0.22f, 0.28f, 0.31f);
			}
		}
	}

	if (nearest < CAD_INFINITY)
	{
		float3 material = cad_material(nearestPrimitive);
		if ((int) nearestPrimitive.kind == 6)
		{
			color = material;
		}
		else
		{
			float3 light = cad_normalize(cad_vec(-0.45f, 0.82f, 0.35f));
			float diffuse = fabs(cad_dot(nearestNormal, light));
			float illumination = 0.28f + 0.72f * diffuse;
			color = cad_vec(material.x * illumination, material.y * illumination, material.z * illumination);
		}
	}

	__global uchar* bytes = (__global uchar*) outputPixels;
	bytes[pixel * 4] = (uchar) cad_channel(color.x);
	bytes[pixel * 4 + 1] = (uchar) cad_channel(color.y);
	bytes[pixel * 4 + 2] = (uchar) cad_channel(color.z);
	bytes[pixel * 4 + 3] = 255;
}
