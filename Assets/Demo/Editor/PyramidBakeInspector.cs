// MIT License

// Copyright (c) 2021 NedMakesGames

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Linq;
using UnityEditor;
using UnityEngine;

// This is a custom inspector for PyramidBakeSettings
[CustomEditor(typeof(PyramidBakeSettings))]
public class PyramidBakeInspector : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        // After drawing the default GUI, add a button to trigger mesh creation
        if(GUILayout.Button("Create")) {
            // Find the unique ID for our compute shader
            var shaderGUID = AssetDatabase.FindAssets("PyramidBuilder").FirstOrDefault();
            if(string.IsNullOrEmpty(shaderGUID)) {
                Debug.LogError("Cannot find compute shader: PyramidBuilder.compute");
            } else {
                // Turn the GUID into the actual compute shader object
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(shaderGUID));

                // Opens a progress bar window
                EditorUtility.DisplayProgressBar("Building mesh", "", 0);
                // Run the baker
                var settings = serializedObject.targetObject as PyramidBakeSettings;
                bool success = PyramidBaker.Run(shader, settings, out var generatedMesh);

                EditorUtility.ClearProgressBar();

                if(success) {
                    SaveMesh(generatedMesh);
                    Debug.Log("Mesh saved successfully");
                } else {
                    Debug.LogError("Failed to create mesh");
                }
            }
        }
    }

    private void SaveMesh(Mesh mesh) {
        // Opens a file save dialog window
        string path = EditorUtility.SaveFilePanel("Save Mesh Asset", "Assets/", name, "asset");
        // Path is empty if the user exits out of the window
        if(string.IsNullOrEmpty(path)) {
            return;
        }

        // Transforms the path to a full system path, to help minimize bugs
        path = FileUtil.GetProjectRelativePath(path);

        // Check if this path already contains a mesh
        // If yes, we want to replace that mesh with the baked mesh while keeping the same GUID,
        // so any other object using it will automatically update
        var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(oldMesh != null) {
            // Clear all mesh data on the old mesh, readying it to receive new data
            oldMesh.Clear();
            // Copy mesh data from the new mesh to the old mesh
            EditorUtility.CopySerialized(mesh, oldMesh);
        } else {
            // Nothing is at this path (or it wasn't a mesh), so create a new asset
            AssetDatabase.CreateAsset(mesh, path);
        }

        // Tell Unity to save all assets
        AssetDatabase.SaveAssets();
    }
}