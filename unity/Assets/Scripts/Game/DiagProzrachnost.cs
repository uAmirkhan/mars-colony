using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace MarsColony.Game
{
    /// <summary>
    /// Диагностика задачи mars-colony-4y9 («прозрачные материалы не рендерятся»):
    /// пять тестовых квадов перед камерой с разными материалами. В редакторе
    /// (Camera.Render в RT) все пять видны и смешиваются с фоном
    /// (diag-prozrachnost.png, 2026-09-06) — проверяем тот же набор в Play
    /// через бэкбуфер окна (SeriyaKadrovUI, действие "kvady").
    /// </summary>
    public static class DiagProzrachnost
    {
        public static readonly string[] Imena = { "A-opaque-red", "B-unlit-transp-green", "C-sprites-blue", "D-alphaclip-magenta", "E-particles-unlit-transp-cyan" };

        public static List<GameObject> Sozdat(Camera cam, float distantsiya)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var mats = new List<Material>();
            var mA = new Material(unlit); mA.color = Color.red; mats.Add(mA);
            var mB = new Material(unlit); Prozrachny(mB); mB.color = new Color(0, 1, 0, 0.7f); mats.Add(mB);
            var mC = new Material(Shader.Find("Sprites/Default")); mC.color = new Color(0, 0, 1, 0.7f); mats.Add(mC);
            var mD = new Material(unlit); mD.SetFloat("_AlphaClip", 1); mD.EnableKeyword("_ALPHATEST_ON"); mD.renderQueue = 2450; mD.color = new Color(1, 0, 1, 1); mats.Add(mD);
            var pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mE = new Material(pShader != null ? pShader : unlit); Prozrachny(mE); mE.color = new Color(0, 1, 1, 0.7f); mats.Add(mE);

            var gos = new List<GameObject>();
            for (int i = 0; i < mats.Count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "diag-" + Imena[i];
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.position = cam.transform.position + cam.transform.forward * distantsiya + cam.transform.right * (-3f + i * 1.5f) * (distantsiya / 6f) + cam.transform.up * 0.5f * (distantsiya / 6f);
                go.transform.rotation = cam.transform.rotation;
                go.transform.localScale = Vector3.one * 1.2f * (distantsiya / 6f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mats[i];
                gos.Add(go);
            }
            return gos;
        }

        /// <summary>Kvad s materialom pyli shattla (Particles/Unlit + list 5x5, okno kadra cherez MaterialPropertyBlock, tint D9895A a0.65):
        /// pered kameroy (dist) ili v zadannoy mirovoy tochke (esli mirovaya != Vector3.zero). Otvechaet na vopros: material/tekstura ili pozitsiya.</summary>
        public static GameObject KvadPyli(Camera cam, float dist, Texture2D tex, int kadr, Vector3 mirovaya, float razmer, string imya)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0xD9 / 255f, 0x89 / 255f, 0x5A / 255f, 0.65f));
            Prozrachny(m); m.DisableKeyword("_ALPHATEST_ON");
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = imya;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = mirovaya != Vector3.zero ? mirovaya : cam.transform.position + cam.transform.forward * dist;
            Vector3 k = cam.transform.position - go.transform.position;
            go.transform.rotation = Quaternion.LookRotation(-k.normalized, Vector3.up);
            go.transform.localScale = Vector3.one * razmer;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
            var blok = new MaterialPropertyBlock(); float shag = 0.2f; int kol = kadr % 5, ryad = kadr / 5;
            blok.SetVector("_BaseMap_ST", new Vector4(shag, shag, kol * shag, 1f - (ryad + 1) * shag));
            blok.SetColor("_BaseColor", new Color(0xD9 / 255f, 0x89 / 255f, 0x5A / 255f, 0.65f));
            r.SetPropertyBlock(blok);
            return go;
        }

        public static void Prozrachny(Material m)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
        }
    }
}
