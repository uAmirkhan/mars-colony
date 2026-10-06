using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarsColony.Game
{
    /// <summary>
    /// Приёмка анимаций интерфейса числом: серия кадров бэкбуфера окна Game
    /// подряд, кадр за кадром, начиная с момента запуска действия. Кадры
    /// копятся в памяти и кодируются в PNG ПОСЛЕ серии: кодирование одного
    /// кадра занимает десятки миллисекунд и иначе съело бы саму анимацию
    /// (открытие склада живёт 120 мс). Инспектор блока 1 получил кадры,
    /// снятые внешним скриптом с опозданием, и увидел только конечное
    /// состояние — этот компонент закрывает ту дыру.
    ///
    /// Запуск из RunCommand в Play: SeriyaKadrovUI.Nachat("sklad", 14, "C:/.../anim/").
    /// Действия: sklad (открыть склад), puzyr (тап по первому товару второго
    /// ряда с запасом), knopka (нажатие кнопки Продать: down, через 4 кадра up),
    /// prodat (продажа с полётом монеты), plashka (плашка имени первого здания).
    /// Имя файла: {deystvie}-{номер}-{мс от старта}.png.
    /// </summary>
    public sealed class SeriyaKadrovUI : MonoBehaviour
    {
        public static void Nachat(string deystvie, int kadrov, string papka)
        {
            var go = new GameObject("seriya-kadrov-ui");
            var s = go.AddComponent<SeriyaKadrovUI>();
            s.StartCoroutine(s.Snyat(deystvie, kadrov, papka));
        }

        readonly List<GameObject> _diagKvady = new List<GameObject>();

        IEnumerator Snyat(string deystvie, int kadrov, string papka)
        {
            System.IO.Directory.CreateDirectory(papka);
            var sklad = FindFirstObjectByType<EkranSklada>(FindObjectsInactive.Include);
            var sost = FindFirstObjectByType<SkladSostoyanie>(FindObjectsInactive.Include);
            if (sklad == null || sost == null)
            {
                Debug.LogError("[seriya] не найден EkranSklada/SkladSostoyanie");
                Destroy(gameObject);
                yield break;
            }

            int indeksRuda = -1;
            for (int i = 0; i < sost.tovary.Length; i++)
                if (sost.tovary[i].row > 0 && sost.tovary[i].qty > 0) { indeksRuda = i; break; }

            // Подготовка: для действий внутри склада он должен быть уже открыт и спокоен.
            if (deystvie == "puzyr" || deystvie == "knopka" || deystvie == "prodat")   // sklad nuzhen tolko etim; dlya shattl on zakryval kadr (seriya 13:15)
            {
                if (!sklad.gameObject.activeSelf) sklad.Perekluchit();
                yield return new WaitForSecondsRealtime(0.4f);
                if (deystvie == "knopka" || deystvie == "prodat")
                {
                    sost.Tap(indeksRuda);
                    yield return new WaitForSecondsRealtime(0.4f);
                }
            }

            var kadry = new List<Texture2D>(kadrov);
            string diag = "";
            var vremena = new List<int>(kadrov);
            yield return null;   // deystvie startuet so svezhego kadra: kadr samoy komandy mosta dlinnyy (50-90 ms) i sedal nachalo animatsii
            float start = Time.unscaledTime;
            GameObject knopkaProdat = null;
            PointerEventData pe = null;
            float shag = 0f;   // pauza mezhdu kadrami; 0 = kazhdyy kadr

            switch (deystvie)
            {
                case "sklad": sklad.Perekluchit(); break;
                case "puzyr": sost.Tap(indeksRuda); break;
                case "prodat": sost.Prodat(); break;
                case "knopka":
                    knopkaProdat = NaytiKnopkuProdat(sost);
                    pe = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                    if (knopkaProdat != null) ExecuteEvents.Execute(knopkaProdat, pe, ExecuteEvents.pointerDownHandler);
                    break;
                case "shattl":
                    {
                        var sh = FindFirstObjectByType<PolyotShattla>();
                        if (sh == null) { Debug.LogError("[seriya] PolyotShattla ne nayden"); break; }
                        diag += "[pylList=" + (sh.pylList == null ? "null" : sh.pylList.name) + " PylSpraytami=" + PolyotShattla.PylSpraytami + "] ";
                        sh.Zapustit();
                        yield return new WaitForSeconds(PolyotShattla.T_KASANIE - 0.05f);
                        start = Time.unscaledTime;
                    }
                    break;
                case "kvady":
                    foreach (var g in DiagProzrachnost.Sozdat(Camera.main, 6f)) _diagKvady.Add(g);
                    {
                        var shp = FindFirstObjectByType<PolyotShattla>();
                        if (shp != null && shp.pylList != null)
                        {
                            var cm = Camera.main;
                            _diagKvady.Add(DiagProzrachnost.KvadPyli(cm, 6f, shp.pylList, 12, cm.transform.position + cm.transform.forward * 6f + cm.transform.up * (-1.2f), 1.5f, "diag-F-pyl-pered-kameroy"));
                            var rend = shp.GetComponentInChildren<Renderer>();
                            var bb = rend.bounds;
                            Vector3 tochka = new Vector3(bb.center.x + bb.extents.x * 1.3f, bb.min.y + 0.3f, bb.center.z);
                            _diagKvady.Add(DiagProzrachnost.KvadPyli(cm, 0f, shp.pylList, 12, tochka, 3f, "diag-G-pyl-u-shattla"));
                            Vector3 tochka2 = new Vector3(bb.center.x, bb.max.y + 2f, bb.center.z);
                            _diagKvady.Add(DiagProzrachnost.KvadPyli(cm, 0f, shp.pylList, 12, tochka2, 3f, "diag-H-pyl-nad-shattlom"));
                            diag += "[G ekran=" + cm.WorldToScreenPoint(tochka).ToString("F0") + " H ekran=" + cm.WorldToScreenPoint(tochka2).ToString("F0") + " bounds=" + bb.ToString("F1") + "] ";
                        }
                    }
                    break;
                case "kvady-daleko":
                    foreach (var g in DiagProzrachnost.Sozdat(Camera.main, 60f)) _diagKvady.Add(g);
                    break;
                case "shattl-polet":
                    {
                        var sh2 = FindFirstObjectByType<PolyotShattla>();
                        if (sh2 == null) { Debug.LogError("[seriya] PolyotShattla ne nayden"); break; }
                        while (sh2.VPolete) yield return null;   // avtozapusk (90 s) mog uzhe podnyat shattl: zhdyom pokoya, inache Zapustit vstayot v ochered (seriya kurs2)
                        start = Time.unscaledTime;
                        sh2.Zapustit();
                        shag = 0.12f;   // ves zahod (snizhenie 1.6 s + zavisanie 0.3 + posadka 0.45) v ~20 kadrov: P12 ten-dekal pod letyashchim shattlom
                    }
                    break;
                case "shattl-vzlet":
                    {
                        var sh3 = FindFirstObjectByType<PolyotShattla>();
                        if (sh3 == null) { Debug.LogError("[seriya] PolyotShattla ne nayden"); break; }
                        while (sh3.VPolete) yield return null;   // to zhe ozhidanie pokoya, chto i u shattl-polet
                        sh3.Zapustit();
                        yield return new WaitForSeconds(PolyotShattla.T_KASANIE + 0.4f);
                        sh3.Zapustit();   // povtornyy zapusk obryvaet stoyanku: vzlet nachinaetsya srazu (zaderzhka 0.5 s)
                        start = Time.unscaledTime;
                        shag = 0.15f;
                    }
                    break;
                case "shattl-stoyanka":
                    {
                        var sh4 = FindFirstObjectByType<PolyotShattla>();
                        if (sh4 == null) { Debug.LogError("[seriya] PolyotShattla ne nayden"); break; }
                        while (sh4.VPolete) yield return null;
                        sh4.Zapustit();
                        yield return new WaitForSeconds(PolyotShattla.T_KASANIE + 0.3f);   // kadry stoyanki: pyl, tryaska, podmena na otkrytyy otsek (0.6 s posle kasaniya)
                        start = Time.unscaledTime;
                        shag = 0.3f;
                    }
                    break;
                case "drony":
                    shag = 0.6f;   // passivnaya semka: nichego ne zapuskaem, prosto smotrim na dvizhenie dronov
                    break;
                case "plashka":
                    var zh = FindFirstObjectByType<ZhivoyInterfeys>();
                    var cam = Camera.main;
                    var zd = FindFirstObjectByType<BuildingClickTarget>();
                    if (zh != null && cam != null && zd != null)
                        zh.PokazatImyaZdaniya(zd.name, cam.WorldToScreenPoint(zd.transform.position), cam);
                    break;
            }

            for (int i = 0; i < kadrov; i++)
            {
                if (deystvie == "knopka" && i == 4 && knopkaProdat != null)
                    ExecuteEvents.Execute(knopkaProdat, pe, ExecuteEvents.pointerUpHandler);
                if (shag > 0f && i > 0) yield return new WaitForSecondsRealtime(shag);
                yield return new WaitForEndOfFrame();
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();
                kadry.Add(tex);
                vremena.Add(Mathf.RoundToInt((Time.unscaledTime - start) * 1000f));
                if (deystvie == "shattl")
                {
                    var spr = GameObject.Find("sprayt-pyli-shattla");
                    int n = 0; foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) if (r.gameObject.name == "sprayt-pyli-shattla") n++;
                    string opis = "net";
                    if (spr != null)
                    {
                        var r = spr.GetComponent<MeshRenderer>(); var cam = Camera.main; var sp = cam.WorldToScreenPoint(spr.transform.position);
                        var mt = r.sharedMaterial;
                        opis = "pos=" + spr.transform.position.ToString("F1") + " ekran=(" + sp.x.ToString("F0") + "," + sp.y.ToString("F0") + "," + sp.z.ToString("F0") + ") scale=" + spr.transform.localScale.x.ToString("F2") + " visible=" + r.isVisible + " shader=" + (mt ? mt.shader.name : "null") + " tex=" + (mt && mt.GetTexture("_BaseMap") ? mt.GetTexture("_BaseMap").name : "null") + " q=" + (mt ? mt.renderQueue.ToString() : "-");
                    }
                    diag += "[" + vremena[vremena.Count - 1] + "ms spraytov=" + n + " " + opis + "] ";
                }
                if (deystvie == "drony")
                {
                    var d1 = GameObject.Find("dron-1"); var d2 = GameObject.Find("dron-2");
                    diag += "[" + vremena[vremena.Count - 1] + "ms d1=" + (d1 == null ? "net" : d1.transform.position.ToString("F1")) + " d2=" + (d2 == null ? "net" : d2.transform.position.ToString("F1"))
                          + (d1 != null && d2 != null ? " mezhdu=" + Vector3.Distance(d1.transform.position, d2.transform.position).ToString("F1") : "") + "] ";
                }
                if (deystvie == "shattl-polet" || deystvie == "shattl-vzlet" || deystvie == "shattl-stoyanka")
                {
                    var tn = GameObject.Find("ten-shattla");
                    var shp2 = FindFirstObjectByType<PolyotShattla>();
                    diag += "[" + vremena[vremena.Count - 1] + "ms ten=" + (tn == null ? "net" : (tn.transform.position.ToString("F1") + " s=" + tn.transform.localScale.x.ToString("F1")))
                              + " shattl=" + (shp2 == null ? "net" : shp2.transform.position.ToString("F1")) + "] ";
                }
                if (deystvie == "prodat")
                {
                    var m = GameObject.Find("moneta-polet");
                    var v = GameObject.Find("vsplyvashka");
                    diag += "[" + vremena[vremena.Count - 1] + "ms moneta=" + (m == null ? "net" : (m.transform.position.x.ToString("F0") + "," + m.transform.position.y.ToString("F0") + " a=" + m.GetComponent<CanvasGroup>().alpha.ToString("F2")))
                          + " vspl=" + (v == null ? "net" : ((RectTransform)v.transform).anchoredPosition.y.ToString("F0")) + "] ";
                }
            }

            for (int i = 0; i < kadry.Count; i++)
            {
                string put = papka + "/" + deystvie + "-" + i.ToString("00") + "-" + vremena[i].ToString("000") + "ms.png";
                System.IO.File.WriteAllBytes(put, kadry[i].EncodeToPNG());
                Destroy(kadry[i]);
            }
            if (diag.Length > 0) Debug.Log("[seriya-diag] " + diag);
            foreach (var g in _diagKvady) if (g != null) Destroy(g);
            Debug.Log("[seriya] " + deystvie + ": " + kadry.Count + " кадров, " + vremena[0] + ".." + vremena[vremena.Count - 1] + " мс, " + papka);
            Destroy(gameObject);
        }

        static GameObject NaytiKnopkuProdat(SkladSostoyanie sost)
        {
            if (sost.popup == null) return null;
            foreach (var b in sost.popup.GetComponentsInChildren<Button>(true))
                if (b.name == "popup-prodat") return b.gameObject;
            return null;
        }
    }
}
