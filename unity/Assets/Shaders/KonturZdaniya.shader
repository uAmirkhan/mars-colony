// Контур выбранного здания — обратный корпус (inverted hull).
// Обоснование приема целиком в комментарии `KonturZdaniya.cs`, здесь только
// сама отрисовка: вершина выносится в СТОРОНУ СИЛУЭТА на `_TolshchinaPx`
// экранных пикселей, видны только обращенные от камеры грани (Cull Front),
// очередь на единицу раньше обычной непрозрачной геометрии — здание,
// нарисованное следом, перекрывает контур изнутри и оставляет только
// кромку по силуэту.
//
// ПОЧЕМУ ПИКСЕЛИ, А НЕ МЕТРЫ (приемка попытки 1, находка 1). Раздутие вдоль
// мировой нормали на фиксированное число метров дает толщину, которая
// зависит от МЕСТНОЙ КРИВИЗНЫ поверхности, а не только от расстояния до
// камеры. Обмер инспектора это поймал числом: медиана 4 px как заявлено, но
// 12% сегментов шире 8 px, максимум 27 px, а восемь мелких ледяных глыб
// слились в кляксу — на маленьком радиусе кривизны тот же метровый отступ
// дает непропорционально большой относительный сдвиг. Здесь вершина сначала
// проецируется как есть, затем — с маленьким пробным шагом по нормали;
// разница двух проекций дает НАПРАВЛЕНИЕ силуэта на экране, а величина
// сдвига берется отдельно, ровно `_TolshchinaPx` пикселей, одна и та же
// везде. Это отвязывает толщину от кривизны совсем: она больше не может
// быть 27 px нигде, потому что не вычисляется из мира — она задается в
// пикселях напрямую.
Shader "MarsColony/KonturZdaniya"
{
    Properties
    {
        _Color("Цвет контура", Color) = (0.373, 0.890, 0.910, 1)
        _TolshchinaPx("Толщина, экранные пиксели", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-1" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Kontur"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _TolshchinaPx;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            // Небольшой пробный шаг вдоль мировой нормали, метров. Нужен
            // только чтобы узнать НАПРАВЛЕНИЕ на экране — величина роли не
            // играет, поэтому взята заведомо маленькой (меньше самого
            // мелкого декора), чтобы не задеть соседнюю кривизну.
            #define PROBNYY_SHAG_M 0.03

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 clipBaza = TransformWorldToHClip(positionWS);
                float4 clipProba = TransformWorldToHClip(positionWS + normalWS * PROBNYY_SHAG_M);

                // В NDC (после деления на w) направление уже не зависит от
                // того, где по глубине стоит вершина — только от того, куда
                // на экране сдвигается силуэт.
                float2 ndcBaza = clipBaza.xy / max(abs(clipBaza.w), 1e-5);
                float2 ndcProba = clipProba.xy / max(abs(clipProba.w), 1e-5);

                // Направление нормализуем В ПИКСЕЛЯХ, а не в NDC. Кадр
                // 1600x900 не квадратный: единица NDC по X и по Y — это
                // разное число пикселей, и нормализация прямо в NDC
                // перекосила бы диагональные направления силуэта, дав им
                // другую итоговую длину в пикселях, чем горизонтальным или
                // вертикальным. Переводим обе точки в пиксели, там же меряем
                // направление и длину, и только потом переводим обратно.
                float2 pikselBaza = ndcBaza * 0.5 * _ScreenParams.xy;
                float2 pikselProba = ndcProba * 0.5 * _ScreenParams.xy;
                float2 napravlenie = pikselProba - pikselBaza;
                float dlina = length(napravlenie);
                napravlenie = dlina > 1e-6 ? napravlenie / dlina : float2(0, 0);

                float2 sdvigPikseley = napravlenie * _TolshchinaPx;
                float2 sdvigNdc = sdvigPikseley * 2.0 / _ScreenParams.xy;

                // Обратно домножаем на w: до деления на w (в растеризаторе)
                // сдвиг в NDC обязан быть домножен на w, иначе аппаратное
                // деление на w его же и отменит.
                clipBaza.xy += sdvigNdc * clipBaza.w;
                OUT.positionHCS = clipBaza;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
