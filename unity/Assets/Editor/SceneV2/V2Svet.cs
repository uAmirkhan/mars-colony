using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Свет, тени и постобработка сцены v2.
///
/// Идет последним из полировочных проходов: полировка теней при мертвом кадре
/// стоит первым пунктом в антицелях задания. Но перед финальной доводкой
/// палитры, а не после - палитра меряется по отрендеренному кадру, и свет
/// двигает ровно те числа, которые она догоняет. Доводить цвет под временным
/// светом значит выбросить работу.
///
/// Три вещи, на которых держится объем в этом жанре:
///   1. контактная тень под каждым объектом, иначе все висит;
///   2. теплый ключевой свет против холодной заливки, иначе кадр плоский;
///   3. тонмаппинг с поднятой насыщенностью, иначе цвет выглядит вылинявшим.
/// </summary>
public static class V2Svet
{
    public const string PROFIL = "Assets/OurAssets/Materials/V2/v2-volume.asset";

    /// <summary>
    /// Азимут солнца В МИРОВЫХ градусах, а не относительно камеры.
    ///
    /// Раньше солнце ставилось как camYaw + 150, то есть привязывалось к
    /// развороту камеры на момент сборки. Облет это ловил сразу: с обратной
    /// стороны кадр уходил в тень, цветность падала со 100 до 84. Сцена,
    /// рассчитанная на облет, обязана иметь солнце в мире.
    /// </summary>
    // 150 градусов выбрано опытом: перебор 150/195/240 при четырех разворотах
    // камеры дал у 150 лучшую худшую сторону.
    public static float Azimut = 150f;

    /// <summary>
    /// Высота солнца над горизонтом. Определяет длину теней, а через нее -
    /// насколько кадр меняется при облете: чем ниже солнце, тем больше площади
    /// уходит в тень с той стороны, откуда смотришь против света.
    /// </summary>
    // 66 градусов выбрано опытом. Перебор 42/55/66 по цветности четырех сторон:
    //   42 -> худшая 85.0, разброс 16.5
    //   55 -> худшая 91.7, разброс 12.7
    //   66 -> худшая 96.0, разброс  9.5
    // Проверено, что это не уплощение ради числа: доля затененного не упала, а
    // выросла с 0.188 до 0.200, светлая и теневая стороны куполов читаются.
    public static float Vysota = 66f;

    public static void Postavit(float camYaw)
    {
        Solnce(camYaw);
        Zalivka();
        Postobrabotka();
    }

    /// <summary>
    /// Ключевой свет. Ставится сзади-слева от камеры: тени падают от зрителя,
    /// а не на обращенные к нему фасады.
    /// </summary>
    private static void Solnce(float camYaw)
    {
        var go = new GameObject("Solnce");
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.92f, 0.80f);
        l.intensity = 1.55f;
        l.shadows = LightShadows.Soft;
        l.shadowStrength = 0.72f;

        // Тень должна касаться основания. Смещение по нормали и по лучу
        // отрывает ее от объекта, и объект начинает выглядеть парящим -
        // при ортографии это особенно заметно, потому что нет перспективной
        // подсказки о том, где земля.
        // Смещение по нормали ВЫТАЛКИВАЕТ тень наружу на величину смещения.
        // При 0.05 солнечная панель толщиной 0.07 теряла тень целиком, и
        // панели с фонарями стояли без контактной тени, пока все соседние
        // объекты ее давали. Поймал смотрящий, числа этого не видят.
        l.shadowBias = 0.008f;
        l.shadowNormalBias = 0.008f;
        l.shadowNearPlane = 0.1f;

        go.transform.rotation = Quaternion.Euler(Vysota, Azimut, 0f);
    }

    /// <summary>
    /// Заливка. Небо холодное, отражение от грунта теплое - это и дает
    /// разницу между освещенной и теневой стороной по ТОНУ, а не только по
    /// светлоте. Ровная серая заливка убивает объем сильнее, чем слабая тень.
    /// </summary>
    private static void Zalivka()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.70f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.40f, 0.36f);
        RenderSettings.ambientGroundColor = new Color(0.36f, 0.22f, 0.16f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;
    }

    /// <summary>
    /// Постобработка. Тонмаппинг обязателен: без него насыщенные краски
    /// упираются в потолок и выцветают на светлых местах.
    ///
    /// Насыщенность поднята намеренно. Замер: цветность нашего кадра 85.7 при
    /// норме Township 102.5 .. 110.5, и это последняя невзятая метрика.
    /// </summary>
    private static void Postobrabotka()
    {
        var dir = Path.GetDirectoryName(PROFIL);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var profil = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profil, PROFIL);

        // КАЖДЫЙ КОМПОНЕНТ НАДО КЛАСТЬ В АССЕТ ОТДЕЛЬНО.
        //
        // profil.Add<T>() заводит компонент в памяти, но в сохраненный ассет он
        // не попадает: профиль на диске остается пустым. Ошибки нет, Volume в
        // сцене есть, renderPostProcessing на камере True, лог чистый - и
        // постобработка не делает ничего, потому что настраивать нечего.
        //
        // Поймано опытом: снимок с Volume и снимок с удаленным Volume дали
        // разницу ровно ноль пикселей, а чтение профиля показало
        // «компонента нет». До этого я успел списать то же самое на cam.Render(),
        // якобы обходящий постобработку, и переписать снимок на запрос
        // конвейера. Гипотеза была неверна, хотя правка полезна сама по себе.
        T Dobavit<T>() where T : VolumeComponent
        {
            var c = profil.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profil);
            return c;
        }

        var tm = Dobavit<Tonemapping>();
        tm.mode.overrideState = true;
        tm.mode.value = TonemappingMode.Neutral;

        var ca = Dobavit<ColorAdjustments>();
        ca.postExposure.overrideState = true; ca.postExposure.value = 0.15f;
        ca.contrast.overrideState = true; ca.contrast.value = 14f;
        // 18, а не 22: после того как купола-теплицы встали прямо и показали
        // свою зелень, цветность кадра выскочила на 111.5 при верхней границе
        // нормы 110.5. Цвет теперь несут объекты, и подкручивать его фильтром
        // больше не нужно.
        ca.saturation.overrideState = true; ca.saturation.value = 20f;

        var bl = Dobavit<Bloom>();
        bl.threshold.overrideState = true; bl.threshold.value = 1.15f;
        bl.intensity.overrideState = true; bl.intensity.value = 0.18f;
        bl.scatter.overrideState = true; bl.scatter.value = 0.6f;

        var vg = Dobavit<Vignette>();
        vg.intensity.overrideState = true; vg.intensity.value = 0.18f;
        vg.smoothness.overrideState = true; vg.smoothness.value = 0.65f;

        EditorUtility.SetDirty(profil);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(PROFIL);
        Debug.Log($"[v2] профиль постобработки: компонентов {profil.components.Count}");

        var go = new GameObject("Postobrabotka");
        var v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 1f;
        v.sharedProfile = profil;
    }

    /// <summary>
    /// Включает постобработку на камере. Без этого Volume в сцене есть, а в
    /// кадре его нет, и никакой ошибки при этом не будет.
    /// </summary>
    public static void VklyuchitNaKamere(Camera cam)
    {
        var d = cam.GetUniversalAdditionalCameraData();
        d.renderPostProcessing = true;
        d.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
    }
}
