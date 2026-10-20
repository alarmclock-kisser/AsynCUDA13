typedef struct
{
	float x;
	float y;
	float z;
} CadVertex;

typedef struct
{
	int type;
	float positionX;
	float positionY;
	float positionZ;
	float scaleX;
	float scaleY;
	float scaleZ;
	int vertexOffset;
	int vertexCount;
} CadPrimitive;

__kernel void cad_transform_vertices(
	__global float* vertexData,
	int vertexCount,
	float translateX,
	float translateY,
	float translateZ,
	float scaleX,
	float scaleY,
	float scaleZ)
{
	int index = get_global_id(0);
	if (index >= vertexCount)
	{
		return;
	}

	CadVertex vertex;
	vertex.x = vertexData[index * 3] * scaleX + translateX;
	vertex.y = vertexData[index * 3 + 1] * scaleY + translateY;
	vertex.z = vertexData[index * 3 + 2] * scaleZ + translateZ;
	vertexData[index * 3] = vertex.x;
	vertexData[index * 3 + 1] = vertex.y;
	vertexData[index * 3 + 2] = vertex.z;
}
