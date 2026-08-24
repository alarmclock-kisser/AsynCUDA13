// Builds a GPU open-addressing hash index over an int key column.
// tableKeys must be pre-initialized to -1 (empty slot sentinel).
// Launched with global size = length (one work-item per key).
__kernel void build_hash_index_int(
	__global const int* keys,
	__global int* tableKeys,
	__global int* tableValues,
	int length,
	int tableSize)
{
	int idx = get_global_id(0);
	if (idx >= length || tableSize <= 0)
	{
		return;
	}

	int key = keys[idx];
	uint hash = ((uint)key * 2654435761u) % (uint)tableSize;

	for (int probe = 0; probe < tableSize; probe++)
	{
		int slot = (int)((hash + (uint)probe) % (uint)tableSize);
		int previous = atomic_cmpxchg(&tableKeys[slot], -1, key);
		if (previous == -1 || previous == key)
		{
			tableValues[slot] = idx;
			return;
		}
	}
}
