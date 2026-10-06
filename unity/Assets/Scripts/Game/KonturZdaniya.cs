using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MarsColony.Game
{
    /// <summary>
    /// Контур выбранного здания: обратный корпус (inverted hull) — дубликат
    /// каждого видимого меша, раздутый в СТОРОНУ СИЛУЭТА на фиксированное
    /// число ЭКРАННЫХ пикселей (не метров, см. ниже), отрисованный с
    /// `Cull Front` в очереди РАНЬШЕ самого здания.
    ///
    /// ПОЧЕМУ ЭТОТ СПОСОБ, А НЕ RENDERER FEATURE. URP без новых пакетов дает
    /// два честных пути: стенсил-контур через ScriptableRendererFeature или
    /// обратный корпус. Feature правит общий Renderer-ассет URP — один файл
    /// на весь проект, и его правка меняет поведение КАЖДОЙ камеры сцены
    /// ради подсветки одного здания. Обратный корпус — обычный компонент
    /// сцены: рождается при выборе, умирает при снятии, ничего чужого не
    /// трогает. Прием старый и предсказуемый (тот же прием, что в Team
    /// Fortress 2 и множестве последующих игр), надежен именно потому, что
    /// не полагается на постобработку кадра.
    ///
    /// ПОЧЕМУ ОЧЕРЕДЬ РАНЬШЕ, А НЕ ZTest Greater. ZTest Greater рисует
    /// контур только там, где он уже перекрыт (глубина больше существующей)
    /// — это контур ПОД предметами переднего плана, а не по силуэту самого
    /// объекта. Здесь очередь Geometry-1: контур пишет глубину первым,
    /// здание, отрисованное следом, перекрывает его изнутри обычным ZTest
    /// LEqual, и остается ровно кромка на границе силуэта — не мигает,
    /// потому что никакого зазора по глубине на границе кадров нет: порядок
    /// отрисовки фиксирован очередью, а не гонкой по расстоянию до камеры.
    ///
    /// ПОЧЕМУ ПИКСЕЛИ, А НЕ МЕТРЫ (правка по итогам приемки попытки 1).
    /// Первая версия раздувала вершину вдоль мировой нормали на фиксированное
    /// число метров. При ортографической камере это дает константную толщину
    /// ТОЛЬКО для плоских поверхностей, а не вообще — на местной кривизне
    /// (мелкие ледяные глыбы, круглые трубы, рама бункера) тот же метровый
    /// отступ дает непропорционально большой сдвиг на экране: обмер
    /// инспектора поймал медиану 4 px при максимуме 27 px и слипшиеся в кляксу
    /// восемь ледяных глыб. Шейдер (`KonturZdaniya.shader`) теперь считает
    /// направление силуэта на экране отдельно от величины сдвига и задает
    /// сдвиг напрямую в пикселях — толщина физически не может убежать за
    /// пределы, которые ей заданы, независимо от кривизны детали.
    /// </summary>
    public sealed class KonturZdaniya : MonoBehaviour
    {
        // Прижато к 2 px по прямому указанию приемки попытки 1: с них
        // начинаем, а не с прежних 4 — 4 при новой, честной пиксельной
        // толщине читалось бы жирнее, чем раньше казалось на глаз.
        private const float TOLSHCHINA_PX = 2.0f;

        // Тёмная кайма (виток "ночь-4", PROMPT-NOCH-2.md строка 2): второй,
        // более широкий inverted-hull, рисуется РАНЬШЕ цианового (см.
        // renderQueue в EnsureMaterial) — цианов уже прикрывает его изнутри,
        // и снаружи остаётся ровно кольцо в 1 px. Шире TOLSHCHINA_PX ровно
        // на 1 px по прямому указанию заказчика ("тёмная кайма 1 px под
        // ним"), не на глаз подобрано.
        private const float TOLSHCHINA_TEMNOY_PX = TOLSHCHINA_PX + 1.0f;

        // Виток "ночь-2" (05.09) увёл контур в тёплый крем #FBEBBA — решение
        // не пережило встречу с куполами: кремовый на светлом куполе не
        // читается вовсе (находка критика кадра, `OTCHET-nochi-2.md` виток 3,
        // пункт 2). Заказчик вернул язык подсветки обратно на яркий циан
        // (PROMPT-NOCH-2.md строка 2: «Контур выделения — обратно ЯРКИЙ
        // циан #5FE3E8»), тот же тон, что и раньше нёс TSVET до правки
        // "ночь-2" и что до сих пор прописан значением по умолчанию в самом
        // шейдере (Properties/_Color) — туда правка "ночь-2" не долезла,
        // молчаливое подтверждение, что это тот самый цвет.
        private static readonly Color TSVET = new Color(0x5F / 255f, 0xE3 / 255f, 0xE8 / 255f);

        // Тёмная кайма — не новый цвет в палитре интерфейса, а тот же навы,
        // что уже несут PlazmaKontur/тело карточки цели в InterfeysBuilder
        // (#0E3B75), альфа 0.8 — по прямому указанию заказчика.
        private static readonly Color TSVET_TEMNYY = new Color(0x0E / 255f, 0x3B / 255f, 0x75 / 255f, 0.8f);

        private const string SHADER_NAME = "MarsColony/KonturZdaniya";

        private static Material _material;
        private static Material _materialTemnyy;

        private readonly List<GameObject> _kopii = new List<GameObject>();

        /// <summary>Включает контур на объекте. Возвращает компонент, которым его потом снять.</summary>
        public static KonturZdaniya Vklyuchit(GameObject na)
        {
            var k = na.AddComponent<KonturZdaniya>();
            k.Postroit();
            return k;
        }

        /// <summary>Сколько теневых мешей построено — 0 значит, что здание
        /// осталось без выделения (нет читаемых MeshRenderer или не собрался
        /// материал). Используется редакторской проверкой всех зданий
        /// (`InterfeysBuilder.KontaktVydeleniya`), а не только на глаз.</summary>
        public int KopiyCount => _kopii.Count;

        /// <summary>Снимает контур и уничтожает себя же — вызывающему больше нечего делать.</summary>
        public void Ubrat()
        {
            foreach (var go in _kopii)
                if (go != null) Unichtozhit(go);
            _kopii.Clear();
            Unichtozhit(this);
        }

        // Object.Destroy откладывает уничтожение до конца кадра и явно
        // отказывается работать вне Play (лог предупреждения, объект
        // остаётся жив) — редакторская проверка всех зданий вызывает
        // Vklyuchit/Ubrat в цикле ВНЕ Play, и без этой развилки контуры
        // прежних зданий копились бы в кадре следующих.
        private static void Unichtozhit(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private void Postroit()
        {
            if (!EnsureMaterial())
                return;

            // GetComponentsInChildren, а не один меш: здание может быть
            // собрано из нескольких MeshRenderer с разными материалами (тело,
            // стекло, металл) — тот же обход, что и в ClickTarget.ClickBounds,
            // ничего нового не изобретаем.
            foreach (var mf in GetComponentsInChildren<MeshFilter>(false))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mf.sharedMesh == null)
                    continue;

                // Тёмная кайма — ПЕРВОЙ (шире, дальше по очереди отрисовки),
                // циан — ВТОРОЙ (уже, перекрывает изнутри). Порядок создания
                // дочерних объектов сам по себе на итоговый визуальный
                // порядок не влияет — рендер сортирует по Material.
                // renderQueue, не по иерархии — но так читается по коду так
                // же, как выглядит на экране.
                SozdatKopiyu(mf, _materialTemnyy, "-kontur-temnyy");
                SozdatKopiyu(mf, _material, "-kontur");
            }
        }

        private void SozdatKopiyu(MeshFilter mf, Material material, string suffiks)
        {
            var go = new GameObject(mf.gameObject.name + suffiks);
            go.transform.SetParent(mf.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mfKopiya = go.AddComponent<MeshFilter>();
            mfKopiya.sharedMesh = mf.sharedMesh;

            var mrKopiya = go.AddComponent<MeshRenderer>();
            mrKopiya.sharedMaterial = material;
            mrKopiya.shadowCastingMode = ShadowCastingMode.Off;
            mrKopiya.receiveShadows = false;
            mrKopiya.lightProbeUsage = LightProbeUsage.Off;
            mrKopiya.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _kopii.Add(go);
        }

        private static bool EnsureMaterial()
        {
            if (_material != null && _materialTemnyy != null)
                return true;

            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[kontur] шейдер {SHADER_NAME} не найден — контур не покажется");
                return false;
            }

            if (_material == null)
            {
                _material = new Material(shader) { name = "mat-kontur-zdaniya" };
                _material.SetColor("_Color", TSVET);
                _material.SetFloat("_TolshchinaPx", TOLSHCHINA_PX);
            }

            if (_materialTemnyy == null)
            {
                _materialTemnyy = new Material(shader) { name = "mat-kontur-zdaniya-temnaya-kayma" };
                _materialTemnyy.SetColor("_Color", TSVET_TEMNYY);
                _materialTemnyy.SetFloat("_TolshchinaPx", TOLSHCHINA_TEMNOY_PX);
                // Material.renderQueue переопределяет тег "Queue" шейдера
                // персонально для ЭТОГО инстанса материала — циановый
                // инстанс тег не трогает и остаётся на Geometry-1 (2999).
                // На единицу раньше — единственное, что гарантирует: тёмная,
                // более широкая кайма ляжет в кадр ДО циана, и циан перекроет
                // её изнутри, а не наоборот (иначе кайма легла бы поверх
                // циана сплошной заливкой, а не тонким кольцом по краю).
                _materialTemnyy.renderQueue = _material.renderQueue - 1;
            }

            return true;
        }
    }
}
