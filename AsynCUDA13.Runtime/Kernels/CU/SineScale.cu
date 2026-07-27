extern "C" __global__ void SineScale(float* data, float scale)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;
    data[i] *= scale;
}
