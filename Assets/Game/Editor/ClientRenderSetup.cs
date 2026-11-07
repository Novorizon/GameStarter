using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public static class ClientRenderSetup
    {
        [MenuItem("Tools/Novorizon/GameStarter/Prepare World Materials")]
        public static void Generate()
        {
            EnsureMaterial("WorldLit", "Universal Render Pipeline/Lit");
            EnsureMaterial("WorldSprite", "Sprites/Default");
            AssetDatabase.SaveAssets();
            ClientResourcesSetup.EnsureConfigured();
            Debug.Log("CLIENT_WORLD_MATERIALS_READY");
        }

        private static void EnsureMaterial(string name, string shaderName)
        {
            const string directory = "Assets/GameAssets/Common/Rendering";
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            string path = directory + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Required world shader is missing: " + shaderName);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.color = Color.white;
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0);
            }

            EditorUtility.SetDirty(material);
        }
    }
}
