# Runtime Pyramid Baker

This system allows you to generate pyramid geometry from source meshes at runtime using compute shaders and `AsyncGPUReadback` instead of blocking `GetData()` calls.

## Key Differences from Editor Version

- **Non-blocking**: Uses `AsyncGPUReadback` instead of `GetData()`
- **Runtime safe**: Can be used in builds, not just the editor
- **Coroutine-based**: Uses Unity coroutines for async operations
- **Automatic cleanup**: Properly manages GPU buffer lifecycle

## Setup Instructions

### 1. Create Settings Asset
1. Right-click in Project window → Create → Own → PyramidBakeSettingsRuntime
2. Configure your source mesh, scale, rotation, and pyramid height

### 2. Setup GameObject
1. Create a GameObject with a `MeshFilter` and `MeshRenderer`
2. Add the `PyramidBakerRuntime` component
3. Assign your settings asset and compute shader
4. Optionally enable "Auto Bake On Start"

### 3. Use the Example Script (Optional)
1. Add the `PyramidBakerExample` component to the same GameObject
2. Assign the baker reference
3. Press Space bar to trigger baking

## How It Works

1. **Setup Phase**: Creates GPU buffers and uploads source mesh data
2. **Dispatch**: Runs the compute shader on the GPU
3. **Async Readback**: Requests GPU data asynchronously
4. **Wait**: Coroutine waits for readback completion
5. **Compose**: Creates the final Unity mesh from readback data
6. **Cleanup**: Releases GPU buffers

## API Reference

### PyramidBakerRuntime
- `StartBake()` - Begin the baking process
- `IsBaking()` - Check if currently baking
- `GetGeneratedMesh()` - Get the final generated mesh

### PyramidBakerExample
- `StartBake()` - Trigger baking from external scripts
- `GetGeneratedMesh()` - Access the generated mesh
- `IsBaking()` - Check baking status

## Performance Notes

- **GPU Memory**: Buffers are created and destroyed for each bake
- **Async**: Main thread is not blocked during GPU processing
- **Batch Processing**: Consider batching multiple meshes if needed
- **Memory Management**: Buffers are automatically cleaned up

## Troubleshooting

- **Missing Compute Shader**: Ensure the compute shader is assigned
- **Missing Settings**: Check that PyramidBakeSettingsRuntime is configured
- **Bake Fails**: Verify source mesh has valid geometry and UVs
- **Performance Issues**: Consider reducing mesh complexity or batching

## Example Usage in Code

```csharp
// Get reference to baker
PyramidBakerRuntime baker = GetComponent<PyramidBakerRuntime>();

// Start baking
if (!baker.IsBaking())
{
    baker.StartBake();
}

// Access result later
Mesh result = baker.GetGeneratedMesh();
if (result != null)
{
    GetComponent<MeshFilter>().mesh = result;
}
```
