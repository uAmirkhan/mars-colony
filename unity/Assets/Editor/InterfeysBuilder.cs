using System.IO;
using MarsColony.Domain;
using MarsColony.Domain.Config;
using MarsColony.Editor;
using MarsColony.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Интерфейс Mars Colony, собираемый кодом в живую сцену MAIN.
///
/// Зачем он заменил прежние два. До этого файла интерфейс существовал в двух
/// несовместимых видах: HudBuilder строил свой холст в ColonyOurs.unity эталоном
/// 1280x800, а узлы, которые реально попадают в кадр приемки, лежали руками в
/// YAML сцены MAIN. Любая правка палитры уходила в файл, который не рендерится,
/// и это уже стоило витка. Точка сборки должна быть одна, и она здесь.
///
/// Числа взяты из ux-interfeys-spec-2026-09-02.md разделы 4.0-4.4 и не
/// выдумываются. Ключевые отличия от прежнего интерфейса, каждое с причиной:
///
/// 1. Тема развернута обратно: панель кремовая, обводка коричневая. Прежняя
///    была темной, то есть инвертированной против арт-библии Township.
/// 2. Счетчиков два, а не четыре. Остается тот, чье число меняется от действия
///    игрока в ближайшую минуту; склад на старте показывает 0/50 и не меняется,
///    у энергии нет механики (развилка D1).
/// 3. Появилась зеленая кнопка действия — единственный элемент интерфейса,
///    которому разрешено догонять мир по насыщенности. В прежнем кадре пикселей
///    цвета 4CAF2E не было ни одного, и жалоба «не знаю куда нажать» шла отсюда.
/// 4. match холста 1.0, а не 0.5: масштаб считается по высоте, и на широком
///    телефоне лишняя ширина уходит миру, а не растягивает панели.
/// 5. Хаб уехал из центра нижней кромки вправо: он закрывал мощеную площадь,
///    единственное живое место кадра.
///
/// Запуск: меню Mars/Interfeys/Sobrat или -executeMethod InterfeysBuilder.Build
/// </summary>
public static class InterfeysBuilder
{
    /// <summary>
    /// Виток "ночь-5", ворота UI-25. Жёлтые бейджи-молнии дефицита питания
    /// ("badge" под корнями "metka-*" в MAIN.unity, читает их
    /// <see cref="MarsColony.Game.VyborZdaniy.Awake"/>) не построены этим
    /// файлом и вообще ни одним живым скриптом репозитория — они уже лежат
    /// запечёнными в сцене (найдено Grep'ом по всему Assets/Editor и
    /// Assets/Scripts: строка "pitaniya" не встречается нигде, кроме самой
    /// .unity сцены и VyborZdaniy, который их только читает). Значит "не
    /// строить" код не может — эквивалент здесь "выключить" на каждой
    /// пересборке, пока флаг false.
    ///
    /// Решение заказчика ночью: убрать бейджи из кадра до появления
    /// механики энергии (у энергии нет геймплея, см. комментарий выше про
    /// "развилка D1") — обратимо, поэтому флаг, а не удаление объектов/кода.
    /// true возвращает прежнее поведение один в один.
    /// </summary>
    public static bool PokazyvatMolnii = false;

    /// <summary>
    /// Включает/выключает существующие бейджи-молнии по флагу
    /// <see cref="PokazyvatMolnii"/>. Вызывается из <see cref="Build"/>, то
    /// есть на каждый batch-прогон (KadrSklad, PilotElementy, Vse), поэтому
    /// действует и без входа в Play — сами объекты и их MeshRenderer живут
    /// в сцене как обычная геометрия, Awake ни при чём.
    /// </summary>
    static void PrimenitMolnii()
    {
        int tronuto = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("metka-")) continue;
            if (t.gameObject.activeSelf == PokazyvatMolnii) continue;
            t.gameObject.SetActive(PokazyvatMolnii);
            tronuto++;
        }
        Debug.Log($"[interfeys] бейджи-молнии (metka-*): PokazyvatMolnii={PokazyvatMolnii}, "
                + $"переключено объектов: {tronuto}");
    }

    // Палитра, раздел 3.1 спеки. Одно значение на роль, без вариантов.
    static readonly Color Krem      = Hex("F7E8C6"); // заливка панели
    static readonly Color KremVerh  = Hex("FFF3DC"); // верх градиента панели
    static readonly Color KremNiz   = Hex("E8D3A8"); // низ градиента панели
    static readonly Color ObvodkaC  = Hex("A8763E"); // обводка панели
    static readonly Color Faska     = Hex("FFFBEE"); // внутренняя кромка
    static readonly Color TekstC    = Hex("6B3E1E"); // текст на креме
    static readonly Color PolkaC    = Hex("C9A768"); // торец кнопки хаба
    static readonly Color KonturKn  = Hex("8A5A2C"); // контур кнопки хаба
    static readonly Color Zoloto    = Hex("F2B705"); // кредиты
    static readonly Color ZolotoRim = Hex("C08A05"); // рим жетона
    static readonly Color Sinyy     = Hex("2E9BE0"); // опыт
    // Кнопка действия — виток UI-6, раздел 2.7 ux-interfeys-referensy-2026-09-03.md.
    // Было: один линейный лерп CtaVerh->CtaNiz (перепад 7 единиц V) плюс
    // тёмная обводка CtaKontur 3-4 px СНАРУЖИ — буквальная инверсия света
    // (у Township там светлая фаска, не тёмная линия). Заменено на
    // семислойную лестницу без внешней обводки вообще: фаска, два ступенчатых
    // градиента, глянцевая полоса поверх стыка градиентов, переходная полоса,
    // тёмный борт-торец. Ход по V теперь считается внутри тела кнопки, а не
    // обводкой поверх него.
    // Толщины полос — не здесь: они считаются в KnopkaDeystviyaSprite() долями
    // высоты кнопки, не канонической сеткой рецепта 420x84. Причина в
    // комментарии над KnopkaDeystviyaSprite.
    static readonly Color CtaFaska        = Hex("37F402"); // фаска сверху
    static readonly Color CtaGrad1Verh    = Hex("0CD402"); // верхний градиент, верх
    static readonly Color CtaGrad1Niz     = Hex("04D700"); // верхний градиент, низ
    static readonly Color CtaGrad2Verh    = Hex("00C700"); // нижний градиент, верх
    static readonly Color CtaGrad2Niz     = Hex("00BE03"); // нижний градиент, низ
    static readonly Color CtaPerehod      = Hex("01A717"); // переходная полоса перед бортом
    static readonly Color CtaBort         = Hex("008912"); // тёмный торец кнопки снизу
    static readonly Color CtaTekstObvodka = Hex("04640C"); // обводка текста кнопки (была CtaKontur)
    static readonly Color TenC      = Hex("3A2418"); // контактная тень
    // Виток UI-13: тёмно-зелёный тинт цоколя кнопки-кита (H≈107°, S≈76%,
    // V≈43% — «около H120 V45» из находки инспектора). Множится через
    // Image.color поверх того же спрайта knopka-bolshaya-zel, что и у самой
    // кнопки — не новый арт, а более тёмная копия того же 9-slice.
    // 2A6E1B давал губу на 16 V темнее тела при критерии 20 — борт Township
    // #008912 против тела #00BE03 это 26 единиц. 1C5212 = V32.
    static readonly Color KnopkaKitTsokol = Hex("1C5212");

    // Порода "Визор" (раздел 2.2 ux-interfeys-referensy-2026-09-03.md). Только
    // для верхней полосы счетчиков — язык мирового HUD, не язык окон. Кремовый
    // корпус (Krem/KremVerh/KremNiz) держал тон 15° против грунта 25°, то есть
    // лежал в том же оранжевом секторе: замер vitok-01-do.png дал дистанцию по
    // тону 10° при пороге 100°. Визор уводит счетчики в hue ~195 — комплемент
    // оранжевого грунта (18°) это 198°, разница становится физической, а не
    // на глаз.
    static readonly Color VizorFon     = HexA("101A24", 0.58f); // заливка визора, обводки/тени/градиента нет
    // Виток UI-7, попытка 1 (отклонена инспектором): карточка цели переведена
    // из кремового Korpus в породу "Визор" одной плоской непрозрачной заливкой
    // #101A24 альфа 1.0. Арифметика непрозрачности была верной — под карточкой
    // самый яркий угол кадра (грунт V100), проект рендерит в Linear, и прогон
    // реального фона (fon-bez-ui.png) через линейное альфа-смешение показал,
    // что холодный тон физически недостижим ни на одной полупрозрачной альфе:
    // визор почти черный, в линейном свете он только гасит красный канал
    // оранжевого грунта, а не перебивает его синевой; тон переходит в холодный
    // сектор только при альфе >0.90, и на 0.90-0.97 ведет себя хаотично (шум
    // почти-черных пикселей). Непрозрачность обязательна и остается.
    //
    // Но инспектор поймал не альфу, а АБСОЛЮТНУЮ ОДНОРОДНОСТЬ: одно значение
    // цвета на все 420x216 без единого пикселя вариации читается как вырез в
    // кадре, а не как HUD-стекло, и плашка "ЦЕЛЬ" сливалась с телом в один
    // плоский прямоугольник. Попытка 2 (эта правка) остается непрозрачной, но
    // добавляет то, чего не бывает у дыры: ход по светлоте (вертикальный
    // градиент), край (кант-волосок по всему периметру, не только под
    // словом), и отдельность слоя плашки (она заметно светлее тела).
    // Строится не через Vizor()/ZalivkaSprite (тот рецепт остается нетронутым
    // для счетчиков и карточки имени — они уже приняты), а через Steklo() /
    // SteklosSprite() ниже, выделенные для этой карточки отдельно.
    // Виток UI-9: насыщенность поднята с 59%/63% до ~76% при том же тоне и
    // той же светлоте. Правый якорь уже стоял на этой породе (UI-8 попытка
    // 2) и дал по кадру vitok-08-popytka2.png медиану насыщенности 60.0% при
    // пороге 65 по рамке 262,14,224,74 — медиана по площади не может уйти
    // выше собственной насыщенности материала, а материал держал только
    // 59-63%. Новые hex посчитаны арифметикой HSV->RGB с зафиксированными H
    // и V и S≈75-76%, не подобраны на глаз: в обоих старом и новом варианте
    // максимальный канал остался B (иначе H или V сместились бы попутно),
    // поэтому V не сдвигается вообще (совпадает до сотых), а H сдвигается
    // на доли градуса.
    static readonly Color KartochkaTseliVerh    = Hex("102842"); // тело карточки, верх градиента: H≈211.2°, S≈75.8%, V≈25.9%
    static readonly Color KartochkaTseliNiz     = Hex("091626"); // тело карточки, низ градиента: H≈213.1°, S≈76.3%, V≈14.9%
    // Плашка "ЦЕЛЬ" — плоская заливка (не градиент, она мала и должна читаться
    // отдельным цельным слоем), светлее верха тела на ~11 V (V≈37% против
    // V≈26% у KartochkaTseliVerh) — с запасом ниже порога V40.
    static readonly Color KartochkaTseliPlashka = Hex("28425E");
    // Кант: не отдельная полупрозрачная заливка (та давала бы утечку сцены
    // ровно по контуру карточки — микро-версию проблемы attempt-1), а цвет,
    // вмешиваемый в локальный фон кольца при выпечке текстуры на силу .a этой
    // константы (0.5), с итоговым альфа-каналом принудительно 1. Тот же прием,
    // что уже несет VizorVolosok (#5FE3E8 поверх визора), просто испечен в
    // текстуру вместо наложения вторым непрозрачным слоем.
    static readonly Color KartochkaTseliKant    = HexA("5FE3E8", 0.5f);
    const int KartochkaTseliKantPx = 2;
    static readonly Color VizorVolosok = HexA("5FE3E8", 0.45f); // волосок по верхней грани, инсет = радиус фаски + 2px
    // Нижний волосок — не по канону раздела 2.2 (там разрешен только верхний),
    // но приемка attempt-2 явно допустила эту правку как способ починить
    // "откушенный" нижний край плашки над темным фоном мира: "либо волосок и
    // снизу, либо поднять непрозрачность". Альфа вдвое ниже верхнего — грань
    // остается второстепенной, иерархия верх/низ из документа не стирается.
    static readonly Color VizorVolosokNiz = HexA("5FE3E8", 0.22f);
    // Виток UI-9: было HexA("101A24", 0.62f). Собственная светлота того
    // цвета (V≈14%) почти совпадала со светлотой низа KartochkaTseliNiz
    // (V≈15%) — пока корпус счетчика был полупрозрачным Vizor() над ярким
    // льдом, трек читался темным пятном на светлом фоне сквозь него; как
    // только корпус стал непрозрачным Steklo() того же навы-тона, у трека и
    // у низа корпуса сошлась почти одна и та же светлота, и граница между
    // ними пропадает арифметически, а не на глаз. Кант рамки полосы не
    // тронут (PlazmaKontur #0E3B75, тот же, что и был) — четвертую роль
    // цианового акцента не завожу. Трек сделан темнее и почти непрозрачным
    // тем же навы-тоном (H≈214°, не новый оттенок): после альфа-смешения с
    // корпусом дает V≈5% против V≈15-26% у корпуса рядом — разница держится
    // светлотой и существующим кантом, а не новым цветом.
    static readonly Color TrekOpyta    = HexA("04070B", 0.95f); // трек прогресс-бара опыта: почти черный навы, темнее корпуса светлотой
    static readonly Color PlazmaVerh   = Hex("36E3F1"); // заполнение прогресс-бара опыта, верх градиента
    static readonly Color PlazmaNiz    = Hex("0982EC"); // заполнение прогресс-бара опыта, низ градиента
    static readonly Color PlazmaKontur = Hex("0E3B75"); // обводка заполнения прогресс-бара

    // Виток UI-12: пилот на готовом ките Wenrexa "UI Casual Free #5" (CC0,
    // Assets/UI/Kit/, лицензия в LICENSE-wenrexa.txt рядом). Заказчик
    // забраковал интерфейс кодовых примитивов и запретил рисовать руками —
    // только "искать готовое и подгонять". Границы 9-slice не подобраны на
    // глаз, а замерены по пикселям обоих PNG построчным сканом (см. отчет
    // витка): у panel-2.png (510x210) кант-кольцо ~10-12px по левому, правому
    // и нижнему краю, а сверху идет непрозрачная лента-шапка, которая физически
    // заканчивается на y=65 от верхнего края текстуры (замерено в 4 разных
    // столбцах x=60/100/400/450 — совпадает с точностью до пикселя) — граница
    // сверху должна включать ЛЕНТУ ЦЕЛИКОМ, иначе она растянется по вертикали
    // и потеряет пропорции. У knopka-bolshaya(-zel).png (210x100) кант и
    // скругление укладываются в ~10-12px со всех четырех сторон одинаково,
    // отдельной нерастяжимой детали сверху там нет (только внутренний
    // градиент тела, который тянется штатно).
    static readonly Vector4 PanelKitBorder  = new Vector4(12, 12, 12, 65); // left, bottom, right, top
    // Проверено на кадре: без вертикальной границы (12,0,12,0) ход по светлоте
    // кнопки падал с 27.7 до 25.5 — проблема не в 9-slice, а в самом спрайте
    // кита: верхняя половина сплошной блик, тёмная часть только в нижних 35%.
    // Градиент усиливается при перекраске (kit_perekraska --v-gradient), а не
    // границами.
    static readonly Vector4 KnopkaKitBorder = new Vector4(12, 12, 12, 12);

    const int RefW = 1600, RefH = 900;
    const string Dir = "Assets/UI/Sobrano";
    const string Scena = "Assets/Scenes/MAIN.unity";

    [MenuItem("Mars/Interfeys/Sobrat")]
    public static void Build()
    {
        Directory.CreateDirectory(Dir);
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);
        Snesti();

        var holst = Holst();
        KartochkaTseli(holst.transform);
        PostroitMayak(holst.transform);
        KartochkaImeni(holst.transform);

        // Экран склада строится ПОСЛЕ карточки цели/маяка/карточки имени, но
        // ДО верхнего HUD-бара (VerhSleva/VerhSprava ниже) — раздел 1 спеки
        // требует полноэкранного модального затемнения, которое перекрывает
        // ВСЁ, кроме верхней полосы счетчиков (её явно называет спека: "звезда
        // опыта слева, счетчик кредитов и кнопка меню справа... остается
        // видна и кликабельна поверх затемнения и панели склада"). Карточка
        // цели, маяк и карточка имени зданий спекой не оговорены отдельно —
        // они часть игрового мира/HUD нижнего яруса, модальный экран их
        // корректно перекрывает, как и положено полноэкранной модалке.
        // Первая версия этой правки клала склад ПЕРЕД карточкой цели — на
        // кадре приемки её зелёная лента перекрывала левую нижнюю ячейку
        // сетки склада, что и поймано на этом же кадре (см. отчет витка).
        var sklad = PostroitEkranSklada(holst.transform);

        VerhSleva(holst.transform);
        VerhSprava(holst.transform);

        // Кнопка меню — единственный вход в склад в этом срезе (раздел 3
        // спеки, пункт 1: "при отсутствии других пунктов меню... открывает
        // Склад напрямую"). Слушатель persistent (UnityEventTools), а не
        // runtime AddListener: этот метод выполняется в редакторе, не в Play,
        // и сцена сохраняется сразу после сборки — обычный AddListener не
        // сериализуется и пропал бы при следующей загрузке сцены.
        var knopkaMenyu = holst.transform.Find("knopka-menyu")?.GetComponent<Button>();
        if (knopkaMenyu != null)
            UnityEditor.Events.UnityEventTools.AddPersistentListener(knopkaMenyu.onClick, sklad.Perekluchit);
        else
            Debug.LogWarning("[interfeys] knopka-menyu не найдена — склад не открыть кнопкой меню");
        // Hab снят 2026-09-04 по решению заказчика: иконок зданий в игре не будет.
        // Вместо ряда подписанных кнопок здание выбирается нажатием прямо в мире —
        // объект обводится контуром и всплывает карточка с названием. Заодно это
        // был худший элемент кадра по замеру: дистанция по тону от мира 10° при
        // пороге 100, тёмный якорь 0.8% при пороге 3, насыщенность 30.5% при 65.
        // Метод Hab() оставлен в файле: иконки понадобятся для содержимого склада,
        // и его примитивы там пригодятся.
        // Hab(holst.transform);

        // Виток UI-3: клик по зданию вместо ряда кнопок. Цели расставляются
        // по именам объектов (см. `ZdaniyaCeliBuilder`), обработчик клика
        // (`VyborZdaniy`) — единственный игровой скрипт этой сцены, кроме
        // самого холста.
        int postavleno = ZdaniyaCeliBuilder.Rasstavit();
        ObespechitMakushkiZdaniy();
        ObespechitVyborZdaniy();
        ObespechitQuestMarker();
        ObespechitEventSystem();
        PrimenitMolnii();

        // Проверяем связи сразу после сборки, а не глазами на кадре: не
        // найденный рантаймом узел молча не обновляется, и интерфейс выглядит
        // собранным при том, что числа на нем краска.
        var zhizn = holst.GetComponent<MarsColony.Game.ZhivoyInterfeys>();
        string net = zhizn != null ? zhizn.ChegoNet() : "рантайма нет вообще";
        if (net != "") Debug.LogError("[interfeys] рантайм не нашел: " + net);
        else Debug.Log("[interfeys] связи рантайма на месте: числа живые");

        // Виток UI-10 требовал одну ссылку на Sprite с миниатюрой карточки;
        // виток UI-11 заменил её упрощённым силуэтом по находке инспектора
        // (детальный спрайт на 32 px схлопывался в пятно). Сверка теперь —
        // что узлы на месте и маяк несёт именно силуэт.
        var mayakImg = holst.transform.Find("mayak-tseli/ikonka")?.GetComponent<Image>();
        if (mayakImg == null || mayakImg.sprite == null)
            Debug.LogError("[interfeys] МАЯК: узел иконки или спрайт не найден");
        else
            Debug.Log("[interfeys] маяк: иконка — " + mayakImg.sprite.name);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[interfeys] собран: 2 счетчика, карточка цели, карточка имени, "
                + $"кликабельных зданий: {postavleno} (ролей в словаре: {ZdaniyaCeliBuilder.SPISOK.Length}), "
                + $"экран склада: {TOVARY_SKLADA.Length} ячеек");
    }

    /// <summary>
    /// Кладет в сцену контроллер выбора здания, если его там еще нет.
    /// Идемпотентно: `Snesti()` холст пересобирает каждый раз, а этот
    /// объект — нет, у него нет данных, которые могли бы устареть.
    /// </summary>
    static void ObespechitVyborZdaniy()
    {
        if (GameObject.Find("VyborZdaniy") != null) return;
        new GameObject("VyborZdaniy").AddComponent<MarsColony.Game.VyborZdaniy>();
        Debug.Log("[interfeys] добавлен VyborZdaniy: клик по зданию — контур и карточка имени");
    }

    /// <summary>
    /// Находка витка "ночь-2", не заказанная этим срезом, но блокирующая
    /// проверку требования: в сцене MAIN не было ни одного `EventSystem`
    /// вообще (ни созданного кодом, ни сохранённого руками в .unity).
    /// `VyborZdaniy` тапы по МИРУ обрабатывает своим лучом в `Update()` и
    /// EventSystem ему не нужен, поэтому дефект был невидим — контур и
    /// карточка имени всегда работали. Но обычные `Button.onClick` (кнопка
    /// меню, "ПОСЕЯТЬ", крест склада, вкладки) идут через штатный uGUI-стек
    /// (`GraphicRaycaster` + `EventSystem`), и без `EventSystem` в сцене
    /// они физически не могут получить клик мыши — реальный тап по кнопке
    /// меню в Play проверялся здесь же (см. отчёт витка) и не открыл склад
    /// ни разу, хотя `EkranSklada.Perekluchit()` подключён к кнопке
    /// персистентным листенером верно. Без этого объекта Часть 3 задания
    /// (кадр со складом, открытым игроком) физически невыполнима — правлю,
    /// хотя тикет об этом не просил впрямую: "чтобы было что нажать" не
    /// работает без него ни для одной кнопки проекта, не только для склада.
    /// Идемпотентно, как и соседние Obespechit*.
    /// </summary>
    static void ObespechitEventSystem()
    {
        if (GameObject.Find("EventSystem") != null) return;
        var go = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        Debug.Log("[interfeys] добавлен EventSystem — без него ни одна Button.onClick (меню, ПОСЕЯТЬ, склад) не кликается");
    }

    /// <summary>
    /// Виток "ночь-4": печёт макушку по вершинам меша (`BuildingClickTarget.
    /// ZapechMakushkuRedaktorom`) для КАЖДОГО кликабельного здания сцены —
    /// тем же приёмом, что и `ObespechitQuestMarker` для купола-цели, только
    /// не для одного объекта, а для всех сразу. Плашка имени здания
    /// (`VyborZdaniy.SchitatTochkuKartochki`) читает испечённое значение в
    /// рантайме и в Play не трогает `Mesh.vertices` вовсе — часть моделей
    /// импортирована с isReadable: 0, и рантаймовый пересчёт на них молча
    /// откатился бы на габарит (см. докстринг полей в BuildingClickTarget).
    /// Идемпотентно: печёт заново поверх старого значения, повторный вызов
    /// на той же сцене только освежает число, ничего не задваивает.
    /// </summary>
    static void ObespechitMakushkiZdaniy()
    {
        var kam = GameObject.Find("kamera");
        var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[interfeys] макушки зданий не испечены — камера 'kamera' не найдена");
            return;
        }

        var zdaniya = Object.FindObjectsByType<MarsColony.Game.BuildingClickTarget>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var z in zdaniya)
            z.ZapechMakushkuRedaktorom(cam);

        Debug.Log($"[interfeys] макушки испечены для {zdaniya.Length} зданий");
    }

    /// <summary>
    /// Виток UI-10: единственный источник текущей цели для карточки и для
    /// маяка. Купол ищется ТОЧНЫМ именем "kupol-geodezicheskiy" (без суффикса
    /// Unity вида " (1)"/" (2)") — это тот же слот 0, на который жёстко
    /// смотрит статический текст карточки ("Посеять водоросли в теплице").
    /// Два других купола-клона намеренно не получают ни маяка, ни блокировки:
    /// это прямое требование проекта onboarding — 0 или 1 маяк в кадре.
    ///
    /// Идемпотентно, как и `ObespechitVyborZdaniy`: повторный запуск на той
    /// же сцене не заводит второй `QuestMarker`.
    /// </summary>
    static void ObespechitQuestMarker()
    {
        var go = GameObject.Find("QuestMarker");
        if (go == null) go = new GameObject("QuestMarker");
        var quest = go.GetComponent<MarsColony.Game.QuestMarker>();
        if (quest == null) quest = go.AddComponent<MarsColony.Game.QuestMarker>();

        var kupol = GameObject.Find("kupol-geodezicheskiy");
        quest.target = kupol;
        if (kupol == null)
        {
            Debug.LogError("[interfeys] цель квеста 'kupol-geodezicheskiy' не найдена в сцене — маяк ставить не на что");
            return;
        }

        // Макушка печётся ЗДЕСЬ, в редакторе, а не в рантайме
        // `QuestMarker.MakushkaMir` — модель купола импортирована с
        // isReadable: 0 (см. комментарий у `QuestMarker.ZapechMakushkuRedaktorom`),
        // и `Mesh.vertices` в Play на ней не читается. Редакторский контекст
        // (этот метод, вызванный вне Play) — единственное надёжное место для
        // этого счёта, поэтому печём один раз при сборке интерфейса.
        var kam = GameObject.Find("kamera");
        var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
        if (cam != null)
        {
            quest.ZapechMakushkuRedaktorom(cam);
            Debug.Log("[interfeys] QuestMarker: макушка испечена по вершинам меша");
        }
        else
        {
            Debug.LogWarning("[interfeys] QuestMarker: камеры нет, макушка не испечена — маяк на первом кадре Play откатится на габарит");
        }

        Debug.Log($"[interfeys] QuestMarker указывает на '{kupol.name}'");
    }

    /// <summary>
    /// Весь виток одной командой: снять иконки с моделей, собрать интерфейс,
    /// снять кадр приемки. Порядок обязателен — сборка берет уже снятые иконки.
    /// </summary>
    [MenuItem("Mars/Interfeys/Vse i kadr")]
    public static void Vse()
    {
        IkonkiIzModeley.Snyat();
        Build();
        Snyat();
    }

    /// <summary>
    /// Кадр приемки: сцена вместе с интерфейсом, 1600x900 — тот же размер, в
    /// котором посчитаны все числа спеки, поэтому единица макета равна пикселю.
    /// </summary>
    /// <summary>Пилот генерированных элементов: два кадра, кнопка A и B (batch, без MCP).</summary>
    public static void PilotElementy()
    {
        foreach (var v in new[] { "a", "b" })
        {
            ElementyVariant = v;
            Build();
            Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/pilot-elementy-" + v + ".png");
        }
    }

    /// <summary>Кадр этапа 3 (правки блока 1 полировки): пересборка + снимок в loop/ui, плюс сохранение сцены.</summary>
    public static void KadrEtap3(string imya)
    {
        Build();
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/" + imya + ".png");
        EditorSceneManager.SaveOpenScenes();
    }

    /// <summary>
    /// Виток "ночь-2": каркас экрана склада. Один процесс batch — два кадра:
    /// склад открыт (приемка сетки/панели) и обычный кадр интерфейса без
    /// него (проверка, что закрытие не оставляет экран в кадре по умолчанию).
    /// </summary>
    [MenuItem("Mars/Interfeys/Kadr sklad")]
    public static void KadrSklad()
    {
        Build();
        // GameObject.Find не видит неактивные объекты — "ekran-sklada" стартует
        // выключенным (см. PostroitEkranSklada), поэтому здесь transform.Find
        // от известного холста, а не Find по имени.
        var holstT = GameObject.Find("interfeys")?.transform;
        var ekran = holstT != null ? holstT.Find("ekran-sklada")?.gameObject : null;
        if (ekran == null) { Debug.LogError("[interfeys] ekran-sklada не найден после Build() — кадр не снят"); return; }

        ekran.SetActive(true);
        // модальное окно гасит всё под собой: карточка цели и её тень прячутся на время кадра
        // (в игре это делает EkranSklada.Perekluchit/Zakryt, редакторский снимок включает экран напрямую)
        var kartT = holstT.Find("kartochka-tseli"); var kartTenT = holstT.Find("kartochka-tseli-ten");
        if (kartT != null) kartT.gameObject.SetActive(false); if (kartTenT != null) kartTenT.gameObject.SetActive(false);
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/noch2-sklad-3.png");
        if (kartT != null) kartT.gameObject.SetActive(true); if (kartTenT != null) kartTenT.gameObject.SetActive(true);

        ekran.SetActive(false);
        ElementyVariant = "a";
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/pilot-elementy-a.png");
    }

    /// <summary>
    /// Три контрольных кадра анимации шаттла (виток "ночь-3", ворота UI-23):
    /// 40% снижения, касание, 60% взлёта — план
    /// `mars-colony/loop/ui/plan-animatsii-shattla.md`, §5.
    ///
    /// Сцена НЕ пересобирается заново (ни `ColonyOursBuilder.Build()`, ни
    /// `Build()` этого файла не зовутся) — берётся уже открытая/сохранённая
    /// MAIN.unity как есть, потому что пересборка стёрла бы ручные правки
    /// владельца и переиграла бы разводку/камеру заново. Единственное
    /// изменение сцены этим методом — компонент `PolyotShattla` на
    /// "shattl-zakrytyy", если его там ещё нет (доктрина "код, не руки":
    /// компонент добавляется кодом при первом прогоне и остаётся в сцене).
    ///
    /// Тряска камеры (план §3) в кадры НЕ идёт: `VystavitVremya` её не
    /// трогает вовсе (см. комментарий в `PolyotShattla.VystavitVremya`) —
    /// иначе пиксельный дифф между тремя кадрами ловил бы сдвиг всей сцены
    /// вместо локальной реакции, план прямо предупреждает об этом капкане.
    /// </summary>
    /// <summary>Назначает PolyotShattla.pylList и проверяет импорт текстуры
    /// (`Assets/UI/Effekty/pyl-klub-list.png`, спрайт-лист 5x5, ohyhei Smoke
    /// Sprite Sheet, CC0): alphaIsTransparency=true, без сжатия, без мипмапов —
    /// иначе альфа-канал листа может уйти в компрессию и клубы выйдут блочными.
    /// AssetDatabase доступен только здесь (Editor-сборка), не в PolyotShattla —
    /// поэтому загрузка и присвоение живут в редакторском коде, а рантайм
    /// пользуется уже сохранённой ссылкой в сцене.</summary>
    private static void ObespechitPylList(PolyotShattla polyot)
    {
        const string put = "Assets/UI/Effekty/pyl-klub-list.png";
        AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceSynchronousImport);

        var imp = (TextureImporter)AssetImporter.GetAtPath(put);
        if (imp == null)
        {
            Debug.LogError("[шаттл] " + put + " не импортировался — TextureImporter не найден, "
                    + "спрайт-пыль останется без текстуры (PylSpraytami молча пропустит квады)");
            return;
        }

        bool menyaem = !imp.alphaIsTransparency
                || imp.textureCompression != TextureImporterCompression.Uncompressed
                || imp.mipmapEnabled;
        if (menyaem)
        {
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(put);
        if (tex == null)
        {
            Debug.LogError("[шаттл] Texture2D по пути " + put + " не загрузилась после импорта");
            return;
        }

        if (polyot.pylList != tex)
        {
            polyot.pylList = tex;
            EditorUtility.SetDirty(polyot);
            Debug.Log("[шаттл] pylList назначен: " + put);
        }
    }

    [MenuItem("Mars/Interfeys/Kadr shattl")]
    public static void KadrShattl()
    {
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.EndsWith("MAIN.unity"))
            EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

        var shattl = GameObject.Find("shattl-zakrytyy");
        if (shattl == null)
        {
            Debug.LogError("[шаттл] shattl-zakrytyy не найден в MAIN.unity — кадры не сняты");
            return;
        }

        var polyot = shattl.GetComponent<PolyotShattla>();
        if (polyot == null) polyot = shattl.AddComponent<PolyotShattla>();
        ObespechitPylList(polyot);

        Vector3 do_ = shattl.transform.position;
        Quaternion doRot = shattl.transform.rotation;

        polyot.VystavitVremya(PolyotShattla.T_TEST_40_SNIZHENIYA);
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/noch2-shattl-1.png");

        polyot.VystavitVremya(PolyotShattla.T_TEST_KASANIE);
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/noch2-shattl-2.png");

        polyot.VystavitVremya(PolyotShattla.T_TEST_60_VZLETA);
        Snyat("C:/Ai/Jarvis/mars-colony/loop/ui/noch2-shattl-3.png");

        polyot.VosstanovitPokoy();

        Vector3 posle = shattl.transform.position;
        Debug.Log($"[шаттл] кадры сняты (t={PolyotShattla.T_TEST_40_SNIZHENIYA:0.00}/"
                + $"{PolyotShattla.T_TEST_KASANIE:0.00}/{PolyotShattla.T_TEST_60_VZLETA:0.00}с); "
                + $"позиция до ({do_.x:0.000},{do_.y:0.000},{do_.z:0.000}) поворот {doRot.eulerAngles}, "
                + $"после ({posle.x:0.000},{posle.y:0.000},{posle.z:0.000}) поворот {shattl.transform.rotation.eulerAngles}");

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("Mars/Interfeys/Snyat kadr")]
    public static void Snyat() { Snyat("C:/Ai/Jarvis/mars-colony/loop/scene-v2/kadry/ui-10-novyy.png"); }

    public static void Snyat(string put)
    {
        // Сцену открываем явно. Отдельным процессом этот метод запускался в
        // пустой сцене, писал «камеры нет» и молча оставлял на диске кадр от
        // прошлого прогона — правки выглядели не применившимися.
        if (!UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.EndsWith("MAIN.unity"))
            EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

        var kam = GameObject.Find("kamera");
        var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
        if (cam == null) { Debug.LogError("[interfeys] камеры нет, снимать нечем"); return; }

        // Окружение пересчитываем явно и ДАЕМ ЕМУ ДОЙТИ. В свежем пакетном
        // процессе сцена открывается без готового окружения, и кадр выходит
        // плоско-оранжевым: постройки принимают цвет грунта, дороги сливаются с
        // ним, тени пропадают. В файле сцены при этом не меняется ни байта, так
        // что отличить это от настоящей поломки можно только сравнением файлов.
        DynamicGI.UpdateEnvironment();
        for (int i = 0; i < 6; i++) { cam.Render(); System.Threading.Thread.Sleep(120); }

        // Буфер обязательно sRGB: проект живет в Linear, и без явного указания
        // линейные числа уходят в PNG как будто они уже sRGB — кадр выходит
        // зелено-серым. Этот дефект в проекте уже ловили дважды.
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tx = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tx.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tx.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        Directory.CreateDirectory(Path.GetDirectoryName(put));
        File.WriteAllBytes(put, tx.EncodeToPNG());
        Object.DestroyImmediate(tx);
        rt.Release();
        Object.DestroyImmediate(rt);
        Debug.Log("[interfeys] кадр снят: " + put);
    }

    /// <summary>
    /// Сносит прежние холсты целиком. Именно целиком, а не по узлам: рукописный
    /// interfeys и кодовый HUD держат разные имена одних и тех же вещей, и
    /// сборка поверх остатков дала бы третий несовместимый вариант.
    /// </summary>
    static void Snesti()
    {
        foreach (var imya in new[] { "interfeys", "HUD" })
        {
            var go = GameObject.Find(imya);
            if (go != null) { Object.DestroyImmediate(go); Debug.Log("[interfeys] снесен прежний " + imya); }
        }
    }

    static Canvas Holst()
    {
        var go = new GameObject("interfeys", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();

        // Режим камеры, а не оверлея: приемка витка идет по кадру, снятому через
        // Camera.Render() в RenderTexture, а оверлейный холст в такой кадр не
        // попадает — интерфейса для судей просто не было бы.
        var kam = GameObject.Find("kamera");
        var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
        if (cam != null) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; }
        else { c.renderMode = RenderMode.ScreenSpaceOverlay; Debug.LogWarning("[interfeys] камеры нет, холст в оверлее"); }
        c.sortingOrder = 100;

        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(RefW, RefH);
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        // 1.0 — масштаб по высоте. При 0.5 на телефоне 20:9 все кегли выходили
        // мельче расчетных, потому что часть масштаба забирала ширина.
        s.matchWidthOrHeight = 1f;

        // Рантайм вешается сразу сборщиком: иначе холст собран, а числа на нем
        // остаются краской — ровно та поломка, из-за которой в сцене и завелся
        // второй рантайм.
        go.AddComponent<MarsColony.Game.ZhivoyInterfeys>();
        return c;
    }

    // ---- верх слева: опыт ---------------------------------------------------

    /// <summary>
    /// Виток UI-8: счетчик кредитов ушел на правый якорь (см. VerhSprava()
    /// ниже). Причина замерена по кадру vitok-07-popytka2.png — рамка правого
    /// верхнего угла 1059,10,530,90 читалась как чистый мир (дистанция по
    /// тону 10° при пороге 100, темных пикселей 1.0% при пороге 3), а весь
    /// верх стоял одной группой слева. Раздел 2.4 референсов дает диагноз
    /// дословно: "без правого якоря композиция валится влево, что у нас и
    /// произошло". Слева остаются звезда уровня и бар XP — их эта правка не
    /// трогает.
    /// </summary>
    static void VerhSleva(Transform root)
    {
        // Виток UI-9: переведен на ту же породу Steklo(), что уже несут
        // карточка цели, кредиты (VerhSprava) и кнопка меню — параметры
        // steklVerh/steklNiz заведены в Schetchik() еще на UI-8 попытке 2
        // именно ради этого переноса, но до сих пор использовались только
        // справа. Причина и числа — в комментарии у Schetchik() выше.
        Schetchik(root, "schetchik-opyt", 28, Forma.Zvezda, Sinyy, "0/12", "1",   // P2 (АД F2): к левому краю, было 264 (12.2%), теперь как карточка цели
                 steklVerh: KartochkaTseliVerh, steklNiz: KartochkaTseliNiz);
    }

    /// <summary>
    /// Правый якорь верхней полосы (раздел 2.4 референсов: "Правый якорь,
    /// x 1059...1568"). Держит счетчик кредитов и системную кнопку меню в
    /// самом углу.
    ///
    /// Изотопы намеренно не заведены: это вторая валюта, которой в игре
    /// сейчас нет — придумывать сущность в дизайне интерфейса не мое право,
    /// это решение владельца (см. отчет витка). Из-за этого якорь физически
    /// тоньше, чем в рецепте 2.4 (который считает медальон изотопов), — это
    /// честная нехватка контента, а не повод раздувать габариты того, что
    /// уже есть.
    ///
    /// x=160 для счетчика кредитов посчитан так, чтобы правый торец его
    /// плашки (блок 216 шириной, флюш с плашкой — см. Schetchik()) встал на
    /// 1600-160=1440, оставив 48 px зазора до левого края кнопки меню
    /// (1600-28-84=1488) — те же пропорции разрыва, что дает раздел 2.4
    /// между медальонами кредитов и изотопов (200 px между центрами при
    /// плашке 160 шириной).
    /// </summary>
    static void VerhSprava(Transform root)
    {
        // Steklo() вместо Vizor(): см. комментарий у Schetchik() про попытку 2
        // и замер vitok-08-pravyy-yakor.png — полупрозрачный визор над
        // оранжевой горой не давал холодного тона физически, непрозрачная
        // порода карточки цели (KartochkaTseliVerh/Niz) не зависит от подложки.
        Schetchik(root, "schetchik-kredity", 160, Forma.Krug, Zoloto, "50", null, Ugol.VerhSprava,
                 KartochkaTseliVerh, KartochkaTseliNiz);
        KnopkaMenyu(root);
    }

    /// <summary>
    /// Кнопка меню — системная, не иконка ресурса и не иконка здания, поэтому
    /// не идет через Ikonka()/Korpus() (тот язык — кремовая керамика окон и
    /// хаба). Замыкает правый угол полосы (раздел 2.4: "Кнопка меню 84x84,
    /// x 1484...1568"). x=28 от правого края — тот же отступ, которым в этом
    /// файле уже размечен левый край (schetchik-kredity стоял на x=28 до этой
    /// правки), симметрия полей сохранена.
    ///
    /// Материал — виток UI-8 попытка 2: было полупрозрачное тело VizorFon
    /// (альфа 0.58) на уже принятой геометрии Steklo(), теперь тело —
    /// непрозрачный градиент KartochkaTseliVerh->KartochkaTseliNiz, та же
    /// порода, что несет карточка цели и (с этой же правки) плашка кредитов
    /// рядом. Причина идентична счетчику кредитов: кнопка стоит над тем же
    /// оранжевым углом кадра, и полупрозрачное затемнение там физически не
    /// дает холодного тона (см. комментарий у Schetchik()). Кант
    /// KartochkaTseliKant не менялся — тот же циан #5FE3E8, что несет волосок
    /// счетчиков и кант карточки цели: языку кадра эта кнопка не добавляет
    /// ничего нового, только дальше несет уже принятый акцент. Круглая (r =
    /// половина стороны), не шестигранная — раздел 2.10 п.5 держит хаб-кнопки
    /// круглыми, шестигранник зарезервирован под оправы медальонов валют, и
    /// раздел 2.9 п.4 ограничивает кадр двумя шестигранниками.
    ///
    /// Глиф — три горизонтальные пилюли (гамбургер): простой силуэт без
    /// мелких деталей, читается на телефоне. Skruglenny() не запекает цвет
    /// в текстуру (тонирует Image.color) — тот же прием, что и у обводки
    /// Korpus(), поэтому здесь нет риска ловушки кэша ZalivkaSprite (раздел
    /// "границы" отчета витка): один и тот же файл-маска безопасно делится
    /// между любым числом разноцветных использований.
    /// </summary>
    static void KnopkaMenyu(Transform root)
    {
        if (ElementyVariant != null) { KnopkaMenyuGen(root); return; }
        const float d = 84f;
        var kn = Steklo(root, "knopka-menyu", 28, 7, d, d, (int)(d / 2f),
                        KartochkaTseliVerh, KartochkaTseliNiz, KartochkaTseliKant, KartochkaTseliKantPx,
                        Ugol.VerhSprava);
        kn.AddComponent<Button>();

        const float barW = 40f, barH = 6f, pitch = 14f;
        for (int i = -1; i <= 1; i++)
        {
            var bar = Uzel(kn.transform, "polosa-" + (i + 1), 0, i * pitch, barW, barH, Ugol.Tsentr,
                          prozrachny: false);
            var bi = bar.GetComponent<Image>();
            bi.sprite = Skruglenny((int)barW, (int)barH, (int)(barH / 2f));
            bi.color = Hex("5FE3E8");
        }
    }

    /// <summary>
    /// Капсула визора с медальоном, выступающим за левый торец плашки на
    /// четверть своей ширины и за верх/низ на треть своей высоты.
    ///
    /// Материал сменен с кремового Korpus() на "Визор" (раздел 2.2
    /// ux-interfeys-referensy-2026-09-03.md): замер vitok-01-do.png дал
    /// дистанцию по тону от мира 10° при пороге 100°, медиану насыщенности
    /// 29.3% при пороге 65, долю темных пикселей 0.6% при пороге 3 — кремовая
    /// плашка была обесцвеченной копией грунта, а не отдельным материалом.
    /// Плашка сжата с 72 до 42 px: цифра при том же кегле теперь берет
    /// половину ее высоты, а не четверть, и медальон 63 px (1.5 ее высоты)
    /// впервые физически выступает за габарит, а не тонет в нем на 4 px.
    ///
    /// Attempt-2: медальон переведен на якорь ОТНОСИТЕЛЬНО плашки (см.
    /// комментарий у "med" ниже) — на attempt-1 он позиционировался от общего
    /// с плашкой блока-контейнера отдельным числом, и на кадре эти два числа
    /// разошлись у счетчика кредитов. Заливка плашки и трека переведена с
    /// Image.color на цвет, запеченный в текстуру (см. Vizor()) — Image.color
    /// с дробной альфой намерялся вдвое слабее заявленного. Добавлен нижний
    /// волосок слабее верхнего — угол XP-плашки терялся на темном фоне мира.
    ///
    /// Виток UI-8: добавлен параметр `blokUgol`, по умолчанию тот же
    /// Ugol.VerhSleva, каким счетчик всегда и строился — существующий вызов
    /// для опыта не меняет поведения ни на пиксель. Нужен он для того, чтобы
    /// счетчик кредитов можно было поставить на правый якорь (VerhSprava) той
    /// же функцией: якорь блока не влияет на то, как позиционируются его
    /// дети (плашка, медальон, волоски) — они считаются от локального
    /// прямоугольника блока, а не от угла холста.
    ///
    /// Виток UI-8, попытка 2: добавлена пара `steklVerh`/`steklNiz`. Замер
    /// попытки 1 (кадр vitok-08-pravyy-yakor.png, рамка 1059,10,530,90) дал
    /// дистанцию по тону от мира 10° при пороге 100 и долю темных пикселей
    /// 2.7% при пороге 3 — обе не сдвинулись. Причина не в геометрии: справа
    /// плашка стоит над ярко-оранжевой горой, а полупрозрачный VizorFon
    /// (альфа 0.58, тело линейного альфа-смешения почти черное) над
    /// оранжевым только гасит красный канал грунта, не перебивает его синевой
    /// — тот же расчет, что уже отклонил такую же полупрозрачную заливку у
    /// карточки цели на UI-7 попытке 1, только там ошибку маскировал лёд под
    /// левым соседом. Слева, где под плашкой лёд, тот же VizorFon держит
    /// холодный тон — поэтому счетчик опыта (VerhSleva, параметры не заданы)
    /// остается на Vizor()/VizorFon нетронутым: это пилот, владелец должен
    /// увидеть кадр "сосед до / сосед после" прежде чем красить класс целиком.
    /// Если оба цвета заданы, плашка строится непрозрачным Steklo() — той же
    /// породой, что карточка цели (UI-7 попытка 2): вертикальный градиент
    /// verh->niz плюс непрозрачный кант KartochkaTseliKant по всему периметру
    /// вместо волосков только по верхней/нижней грани. Тёмный якорь теперь
    /// несет материал (градиент опущен до V≈15% на нижней трети), а не
    /// альфа-смешение с тем, что лежит под плашкой.
    ///
    /// Виток UI-9: пилот принят классом целиком. Замер vitok-08-popytka2.png
    /// поймал именно то, о чем предупреждал абзац выше, — левый якорь
    /// (VerhSleva) остался на полупрозрачном Vizor()/VizorFon и вышел
    /// блеклой линялой плашкой рядом с непрозрачным правым (медиана
    /// насыщенности 46.8% при пороге 65 по рамке 262,14,224,74). Теперь
    /// VerhSleva тоже передает steklVerh/steklNiz — оба счетчика, кнопка
    /// меню и карточка цели стоят на одной непрозрачной породе. Заодно у
    /// самих KartochkaTseliVerh/Niz поднята насыщенность (см. комментарий у
    /// констант) — без этого медиана упиралась в потолок собственной
    /// насыщенности материала (59-63%), ниже порога 65 физически.
    /// </summary>
    static void Schetchik(Transform root, string imya, float x, Forma f, Color tsvet,
                          string chislo, string uroven, Ugol blokUgol = Ugol.VerhSleva,
                          Color? steklVerh = null, Color? steklNiz = null)
    {
        if (ElementyVariant != null) { SchetchikGen(root, imya, x, f, chislo, uroven, blokUgol, ElementyVariant); return; }
        const float korpusH = 42f;               // раздел 2.5: плашка вдвое ниже прежней (72 -> 42)
        const float medH = korpusH * 1.5f;        // раздел 2.5: медальон 1.5 от высоты плашки
        // Доля медальона, выступающая за левый торец плашки наружу.
        const float medVystupDolya = 0.25f;

        // Корпус смещен на 20 от левого торца блока — контейнер шире плашки
        // ровно настолько, чтобы медальону было куда выступать влево, не
        // обрезаясь габаритом блока.
        var blok = Uzel(root, imya, x, 28, 216, 72, blokUgol, prozrachny: true);
        // SteklosSprite() считает градиент долей высоты (y/(h-1)), а не
        // абсолютными пикселями карточки цели (216) — на плашке 42 px тот же
        // вызов дает тот же относительный ход тона, числа с карточки сюда не
        // переносятся и переносить нечего.
        var korpus = steklVerh.HasValue
            ? Steklo(blok.transform, "korpus", 20, 0, 196, korpusH, 14,
                     steklVerh.Value, steklNiz.Value, KartochkaTseliKant, KartochkaTseliKantPx,
                     Ugol.SverhuSleva)
            : Vizor(blok.transform, "korpus", 20, 0, 196, korpusH, 14, Ugol.SverhuSleva);

        // Attempt-3: общий y=8 для обеих цифр (из attempt-2) оказался верен
        // только для опыта — там под числом стоит бар, и стопка "число+бар"
        // держит центр 49.5 против центра плашки 48.5. У кредитов бара нет,
        // и то же +8 утащило "50" к самому потолку плашки (отступ сверху 2 px
        // против 14 снизу). Опыт не трогаю, у кредитов сдвигаю вниз на 4 px
        // (y 8 -> 4): отступы становятся 5/10 вместо 2/14. Цвет — белый: визор
        // темный, коричневый TekstC на нем читался бы почти черным пятном.
        var chis = Uzel(korpus.transform, "znachenie", -20, uroven == null ? 4 : 8, 132, 44,
                        Ugol.Sprava, prozrachny: true);
        Nadpis(chis, chislo, uroven == null ? 34 : 26, TextAnchor.MiddleRight, Color.white, mono: true);

        if (uroven != null)
        {
            // Обводка полосы — раздел 2.6/4.6 референсов, #0E3B75. Кладется
            // соседом ПЕРЕД телом полосы: ребенок в uGUI рисуется поверх
            // родителя, обводка-ребенок закрасила бы полосу целиком (тот же
            // урок, что и в Korpus()).
            const int bpx = 2;
            const float barX = 68f, barY = 3f, barW = 112f, barH = 14f;
            var obv = Uzel(korpus.transform, "polosa-obvodka", barX - bpx, barY - bpx,
                          barW + bpx * 2, barH + bpx * 2, Ugol.SnizuSleva, prozrachny: false);
            obv.GetComponent<Image>().sprite = Skruglenny((int)(barW + bpx * 2), (int)(barH + bpx * 2), 7 + bpx);
            obv.GetComponent<Image>().color = PlazmaKontur;

            // Трек темный прозрачный, а не теплый Dorozhka на креме: было 14
            // единиц контраста V к фону панели — трек был физически невидим.
            // Цвет запечен в текстуру (ZalivkaSprite), Image.color = white — та
            // же правка, что и в Vizor(): фракционная альфа через Image.color
            // намерялась вдвое слабее заявленной на приемке attempt-1.
            var dor = Uzel(korpus.transform, "polosa", barX, barY, barW, barH, Ugol.SnizuSleva, prozrachny: false);
            var di = dor.GetComponent<Image>();
            di.sprite = ZalivkaSprite((int)barW, (int)barH, 7, TrekOpyta, "trek"); di.color = Color.white;

            var zap = Uzel(dor.transform, "zapolnenie", 0, 0, barW, barH, Ugol.Rastyanut, prozrachny: false);
            var zi = zap.GetComponent<Image>();
            // Заполнение — вертикальный градиент "плазмы" (#36E3F1 -> #0982EC),
            // тот же рецепт, что у полосы вместимости склада. KorpusSprite
            // красит цвет прямо в текстуру, поэтому Image.color оставляем
            // белым — иначе тон перемножится и потемнеет вдвое.
            zi.sprite = KorpusSprite((int)barW, (int)barH, 7, PlazmaVerh, PlazmaNiz, 0, PlazmaVerh);
            zi.color = Color.white;
            zi.type = Image.Type.Filled; zi.fillMethod = Image.FillMethod.Horizontal; zi.fillAmount = 0f;
        }

        // Медальон — РЕБЕНОК ПЛАШКИ (korpus), а не блока-контейнера. Раньше
        // медальон и плашка позиционировались от общего блока НЕЗАВИСИМЫМИ
        // числами, и приемка attempt-2 намерила на кадре, что у счетчика
        // кредитов эти числа разошлись: плашка встала не там, где предсказывал
        // код, монета осталась на прежнем месте, и на плашку заходило 7-15 px
        // монеты из 61 вместо трех четвертей. У звезды опыта то же самое
        // совпадение чисел просто не разошлось, но это везение, а не гарантия.
        // Якорь SlevaTsentr берет координаты РЕАЛЬНОГО прямоугольника korpus:
        // по вертикали медальон садится точно на середину плашки (пивот 0.5
        // сам центрирует, посчитать вручную нечего — совпадение по вертикали
        // у обоих счетчиков теперь гарантировано Unity, а не арифметикой),
        // по горизонтали смещен на четверть своей ширины влево, наружу.
        var med = Uzel(korpus.transform, "medalyon", -medH * medVystupDolya, 0, medH, medH,
                       Ugol.SlevaTsentr, prozrachny: false);
        var mi = med.GetComponent<Image>();
        // Медальон — готовая иконка заказчика (znachok-kredity / znachok-opyt-xp
        // из Assets/UI/Ikonki/resursy), а не рисованный круг/звезда: заказчик
        // назвал заглушки «нулевой детализацией». Рисованная форма остаётся
        // резервом, если файла нет.
        var gotovaya = SpriteResursa(f == Forma.Krug ? "znachok-kredity" : "znachok-opyt-xp");
        if (gotovaya != null)
        {
            mi.sprite = gotovaya; mi.color = Color.white;
            if (uroven != null)
            {
                var u0 = Uzel(med.transform, "uroven", 0, -2, medH, medH * 40f / 64f, Ugol.Tsentr, prozrachny: true);
                Nadpis(u0, uroven, 26, TextAnchor.MiddleCenter, Color.white, mono: true);
                u0.AddComponent<Outline>().effectColor = Hex("0E3B75");
            }
            return;
        }
        mi.sprite = FormaSprite(f, (int)medH);
        // У монеты снаружи темный рим, внутри светлое поле. Наоборот жетон
        // читается дыркой: темный центр глаз принимает за отверстие.
        mi.color = f == Forma.Krug ? ZolotoRim : tsvet;

        if (f == Forma.Krug)
        {
            float poleH = medH - 12f; // те же 6 px рима с каждой стороны, что и раньше (52 из 64)
            var pole = Uzel(med.transform, "pole", 0, 0, poleH, poleH, Ugol.Tsentr, prozrachny: false);
            var ri = pole.GetComponent<Image>();
            ri.sprite = FormaSprite(Forma.Krug, (int)poleH); ri.color = tsvet;
        }
        if (uroven != null)
        {
            var u = Uzel(med.transform, "uroven", 0, 0, medH, medH * 40f / 64f, Ugol.Tsentr, prozrachny: true);
            Nadpis(u, uroven, 26, TextAnchor.MiddleCenter, Color.white, mono: true);
        }
    }

    // ---- низ слева: карточка цели -----------------------------------------

    /// <summary>
    /// Пилот генерированных элементов (2026-09-05): панель с лентой и кнопка —
    /// картинки заказчика из Gemini по паспорту арт-директора, нарезанные
    /// element_narezka.py. "a" — тёплая пилюля Township, "b" — холодный
    /// скруглённый прямоугольник с глянцевой полосой. null — старый кит Wenrexa.
    /// </summary>
    public static string ElementyVariant = "a";

    static void KartochkaTseli(Transform root)
    {
        if (ElementyVariant != null) { KartochkaTseliGen(root, ElementyVariant); return; }
        // Виток UI-7, попытка 2: тело карточки остается непрозрачной породой
        // "Визор" по духу (та же семья цвета, тот же язык HUD-стекла), но
        // строится через Steklo()/SteklosSprite(), не через Vizor(): та
        // функция и ее плоская ZalivkaSprite() остаются нетронутыми для
        // счетчиков и карточки имени, которые уже приняты на прежнем рецепте.
        // Здесь вместо однородной заливки — вертикальный градиент
        // KartochkaTseliVerh->KartochkaTseliNiz и непрозрачный кант-волосок по
        // всему периметру (см. комментарий у констант выше и у Steklo()).
        // Тени нет: у "Визора" ее не бывает по определению породы (раздел 2.2
        // референсов), юбка-тень Korpus() (ten: 0.38f) сюда не переносится.
        // Виток UI-12 (пилот на готовом ките, см. комментарий у PanelKitBorder
        // выше): тело карточки — panel-2.png, 9-slice, вместо Steklo(). Прежний
        // Steklo()/SteklosSprite() не тронут — им еще стоят счетчики (Schetchik)
        // и кнопка меню (KnopkaMenyu), их не красим до "да" заказчика.
        // 420x256 вместо 420x216: лента-шапка кита (65 px) плюс две строки
        // задания плюс кнопка 76 в 216 не помещались — кнопка съедала вторую
        // строку. Низ опущен со 100 до 64, чтобы верхняя грань осталась там же.
        // Виток UI-13, находка 1: у Township-панелей своя мягкая контактная
        // тень на игровой мир под ними — карточка стояла «наклеенной» без нее.
        // Тот же прием, что и юбка-тень Korpus(ten:) / knopka-deystviya-ten
        // ниже: TenSprite + TenC. Узел — sibling ПЕРЕД kartochka-tseli (родитель
        // тот же root), поэтому рисуется первым и лежит под карточкой.
        // Размер — карточка + 2х12 px (тот же запас, что у Korpus/CTA), вниз
        // смещена на 5+... итог 6 px по договоренности находки, альфа 0.35
        // (внутри самого TenSprite уже зашит box-blur в три прохода — тот же
        // визуальный радиус 8-10 px, что несут остальные тени интерфейса).
        var kartTen = Uzel(root, "kartochka-tseli-ten", 28 - 12, 64 - 12 - 8, 420 + 24, 256 + 24,
                            Ugol.SnizuSleva, prozrachny: false);
        var kti = kartTen.GetComponent<Image>();
        // Размытие 12, не 20: с 20 тень у самой кромки давала 8.3 V разницы с
        // миром при критерии 12 — плотность уходила в широкий ореол.
        kti.sprite = TenSprite(420, 256, 12);
        // 0.35 давало под нижней гранью всего 5.7 V разницы с миром при критерии
        // 12: размытие 20 растягивает тень и гасит её у самой кромки. 0.5 — с
        // запасом, но не чернота: Township кладёт под панель именно мягкую тень.
        kti.color = new Color(TenC.r, TenC.g, TenC.b, 0.5f);

        var kart = Uzel(root, "kartochka-tseli", 28, 64, 420, 256, Ugol.SnizuSleva, prozrachny: false);
        var kartImg = kart.GetComponent<Image>();
        kartImg.sprite = SpriteKita("panel-2", PanelKitBorder);
        kartImg.type = Image.Type.Sliced;
        kartImg.color = Color.white;

        // Прежняя плашка-Steklo со скобами (см. историю витков UI-7..UI-9)
        // снесена целиком: заголовок теперь садится прямо на готовую
        // ленту-шапку спрайта, у нее уже есть собственный объем и кант —
        // вторая накладная плашка поверх нее была бы лишним слоем.
        // Кегль и якорь — те же координаты, что держала прежняя плашка
        // (x=24 от левого края, у верхней грани), только без своего корпуса.
        // Обводка темным навы вместо простого белого — лента яркая
        // (почти голубая), белый текст без обводки на ней проваливается по
        // контрасту в отличие от текста на темном теле карточки ниже.
        // Лента-шапка спрайта panel-2 занимает верхние 65 px карточки (border
        // top, не растягивается). Заголовок сидит внутри неё, а не над гранью:
        // при y=+12 он вылезал на кант, а тело задания ложилось на саму ленту.
        var pt = Uzel(kart.transform, "tekst-tsel", 24, -12, 150, 40, Ugol.SverhuSleva, prozrachny: true);
        Nadpis(pt, "ЦЕЛЬ", 24, TextAnchor.MiddleLeft, Color.white);
        var ptObv = pt.AddComponent<Outline>();
        ptObv.effectColor = KartochkaTseliVerh; ptObv.effectDistance = new Vector2(1.5f, -1.5f);

        // Миниатюра цели — та же картинка, что горит маркером над целью в мире.
        // Связь «слово это место» держится изображением, текстом она не держится.
        // Не перерисована и не перекрашена — вне границ этого витка.
        var mini = Uzel(kart.transform, "miniatyura", 24, -74, 56, 56, Ugol.SverhuSleva, prozrachny: false);
        var mim = mini.GetComponent<Image>();
        mim.sprite = Ikonka(0);
        mim.color = Color.white;

        // Тело задания было TekstC (темно-коричневый) на креме — на темном
        // навы это черный текст на черном. Белый: тот же прием, что и у чисел
        // счетчиков на этой же породе (Schetchik() выше, комментарий "визор
        // темный, коричневый TekstC на нем читался бы почти черным пятном").
        var telo = Uzel(kart.transform, "telo", 92, -72, 296, 68, Ugol.SverhuSleva, prozrachny: true);
        Nadpis(telo, "Посеять водоросли\nв теплице", 26, TextAnchor.UpperLeft, Color.white);
        telo.GetComponent<Text>().lineSpacing = 1.14f;

        // Награда стоит непосредственно над кнопкой: прежде квитанция «+1 XP»
        // висела над пустотой, то есть чек без сделки.
        // Звезда и число — два независимых узла, а не вложенные. Вложенная
        // звезда внутри текстового узла уезжала от расчетной позиции, и число
        // «4 XP» выходило за правый край карточки обрезанным.
        // Цвет звезды (Sinyy, #2E9BE0) не менялся: это тот же синий, которым
        // уже стоит звезда уровня на счетчике опыта поверх той же породы
        // "Визор" — пара проверена приемкой того витка, контраст к V≈14 фона
        // порядка 74 единиц V, читается без дополнительной обводки.
        var zv = Uzel(kart.transform, "zvezda-nagrady", 96, 98, 28, 28, Ugol.SnizuSprava, prozrachny: false);
        var zi2 = zv.GetComponent<Image>();
        zi2.sprite = FormaSprite(Forma.Zvezda, 28); zi2.color = Sinyy;

        // "4 XP" было коричневым ObvodkaC — теперь белым, тем же приемом, что
        // и тело задания выше.
        var nag = Uzel(kart.transform, "nagrada", 24, 96, 62, 30, Ugol.SnizuSprava, prozrachny: true);
        Nadpis(nag, "4 XP", 24, TextAnchor.MiddleRight, Color.white, mono: true);

        // Кнопка действия. Единственное насыщенное пятно интерфейса.
        //
        // Виток UI-6: раньше это был вызов Korpus() — один линейный лерп плюс
        // тёмная обводка снаружи. Korpus() не умеет многоступенчатый градиент,
        // фаску и глянцевую полосу (раздел 2.7), а переписывать его сигнатуру
        // нельзя — им собраны все остальные кремовые панели интерфейса. Поэтому
        // тело кнопки рисует отдельная текстура KnopkaDeystviyaSprite(), а тень
        // под кнопкой собрана вручную тем же приёмом, что и внутри Korpus()
        // (TenSprite + TenC), чтобы юбка-тень не пропала вместе с вызовом.
        // Виток UI-13: тень опущена еще на 6 px вниз, чтобы лежать под новым
        // цоколем ниже, а не под самой кнопкой (иначе цоколь визуально висел
        // бы над своей же тенью).
        var ctaTen = Uzel(kart.transform, "knopka-deystviya-ten", 24 - 12, 16 - 12 - 5 - 6, 372 + 24, 76 + 24,
                          Ugol.SnizuSleva, prozrachny: false);
        var cti = ctaTen.GetComponent<Image>();
        cti.sprite = TenSprite(372, 76, 38);
        cti.color = new Color(TenC.r, TenC.g, TenC.b, 0.20f);

        // Виток UI-13, находка 2: у Township-кнопки снизу видимая тёмная
        // «губа» ~6 px (документ 2.7: борт #008912 6 px) — у спрайта кита низ
        // мягкий градиент без нее. Нового арта нет, поэтому цоколь — та же
        // сама текстура кнопки (SpriteKita, тот же border), сдвинутая на 6 px
        // вниз и затемненная тинтом KnopkaKitTsokol. Кнопка встает поверх
        // (следующий узел, тот же x/w/h) и закрывает весь цоколь кроме нижних
        // 6 px — это и есть видимая губа-цоколь без нового спрайта.
        var tsokol = Uzel(kart.transform, "knopka-tsokol", 24, 16 - 6, 372, 76, Ugol.SnizuSleva, prozrachny: false);
        var tsi = tsokol.GetComponent<Image>();
        // Свой спрайт без канта и блика (knopka-tsokol.png — та же кнопка,
        // сплющенная в V 28-36): инспектор UI-13 прочёл копию кнопки с её
        // бирюзовой обводкой как «вторую кнопку», а не как губу.
        tsi.sprite = SpriteKita("knopka-tsokol", KnopkaKitBorder) ?? KnopkaDeystviyaKitSprite();
        tsi.type = Image.Type.Sliced;
        // Цвет несёт сам спрайт; множитель белый, иначе губа уходит в черноту.
        tsi.color = Color.white;

        // Виток UI-12: тело кнопки — готовый спрайт кита (зеленый вариант,
        // откат на синий, если художник его еще не привез — см.
        // KnopkaDeystviyaKitSprite()), 9-slice, вместо процедурной
        // KnopkaDeystviyaSprite(). Та функция не удалена: если пилот
        // отклонят, откат на нее — правка одной строки.
        var cta = Uzel(kart.transform, "knopka-deystviya", 24, 16, 372, 76, Ugol.SnizuSleva, prozrachny: false);
        var ci = cta.GetComponent<Image>();
        ci.sprite = KnopkaDeystviyaKitSprite();
        ci.type = Image.Type.Sliced;
        ci.color = Color.white;
        cta.AddComponent<Button>();
        var ct = Uzel(cta.transform, "tekst", 0, 6, 372, 44, Ugol.Tsentr, prozrachny: true);
        Nadpis(ct, "ПОСЕЯТЬ", 30, TextAnchor.MiddleCenter, Color.white);
        var obv = ct.AddComponent<Outline>();
        obv.effectColor = CtaTekstObvodka; obv.effectDistance = new Vector2(2f, -2f);
        var ctaTenTeksta = ct.AddComponent<Shadow>();
        ctaTenTeksta.effectColor = new Color(0f, 0f, 0f, 0.5f);
        ctaTenTeksta.effectDistance = new Vector2(0f, -2f);
    }

    // ---- экран склада (виток "ночь-2") -------------------------------------

    /// <summary>Одна позиция сетки склада: ключ товара (совпадает с `GoodId` в
    /// `src/domain/config/goods.ts`), файл иконки и стартовый запас-заглушка.</summary>
    struct TovarSklada { public string id; public string ikonka; public int qty; }

    /// <summary>
    /// Запасы и порядок позиций — заглушка. Unity-сцена этого проекта не имеет
    /// живого моста к TS-домену (`mars-colony/src`), поэтому числа здесь
    /// константы, а не чтение настоящего состояния игры.
    ///
    /// TODO(sklad-live): когда мост появится, заменить перечисление и `qty`
    /// на чтение `ColonyState.warehouse` (количество) и
    /// `WAREHOUSE_START_CAPACITY`/`WAREHOUSE_UPGRADE_STEP`/`WAREHOUSE_MAX_CAPACITY`
    /// из `economy.ts` — не переносить числа руками второй раз, посчитать один
    /// раз в мосте и отдать сюда.
    ///
    /// Порядок — категория (грядка -> фабрика/добыча), внутри категории по
    /// уровню открытия, буквально `spec-sklad.md` раздел 4.1. Список и имена
    /// файлов сверены с `goods.ts` (17 `GoodId`, все иконки уже лежат в
    /// `Assets/UI/Ikonki/resursy/`). Фильтр по уровню игрока (спека 2.4:
    /// "только разблокированные товары показываются вообще") этот виток НЕ
    /// делает — задание виток 1 явно просит сетку из ВСЕХ товаров ("каркас"),
    /// фильтр по уровню — материал следующего витка, когда склад свяжут с
    /// живым состоянием игрока.
    /// </summary>
    static readonly TovarSklada[] TOVARY_SKLADA =
    {
        // Грядка (crop)
        new TovarSklada{ id = "algae",        ikonka = "znachok-vodorosli",       qty = 12 },
        new TovarSklada{ id = "soy",          ikonka = "znachok-soya",            qty = 8  },
        new TovarSklada{ id = "mushrooms",    ikonka = "znachok-griby",           qty = 0  },
        new TovarSklada{ id = "tomatoes",     ikonka = "znachok-tomaty",          qty = 0  },
        new TovarSklada{ id = "cotton",       ikonka = "znachok-khlopok",         qty = 0  },
        new TovarSklada{ id = "coffee_beans", ikonka = "znachok-kofe",            qty = 0  },
        // Фабрика/добыча (factory)
        new TovarSklada{ id = "protein_bar",   ikonka = "znachok-protein-batonchik", qty = 4  },
        new TovarSklada{ id = "mushroom_soup", ikonka = "znachok-gribnoy-sup",       qty = 0  },
        new TovarSklada{ id = "regolith",      ikonka = "znachok-regolit",           qty = 15 },
        new TovarSklada{ id = "water_ice",     ikonka = "znachok-vodyanoy-led",      qty = 6  },
        new TovarSklada{ id = "water",         ikonka = "znachok-voda",              qty = 0  },
        new TovarSklada{ id = "iron_ore",      ikonka = "znachok-ruda-zheleznaya",   qty = 0  },
        new TovarSklada{ id = "oxygen_tank",   ikonka = "znachok-kislorod-ballon",   qty = 0  },
        new TovarSklada{ id = "fabric",        ikonka = "znachok-tkan-sint",         qty = 0  },
        new TovarSklada{ id = "methane",       ikonka = "znachok-metan",             qty = 0  },
        new TovarSklada{ id = "jumpsuit",      ikonka = "znachok-kombinezon",        qty = 0  },
        new TovarSklada{ id = "coffee_ration", ikonka = "znachok-kofe-payok",        qty = 0  },
    };

    // WAREHOUSE_START_CAPACITY, economy.ts — тот же виток заглушки, что и
    // у TOVARY_SKLADA выше, тем же TODO.
    const int SKLAD_EMKOST = 50;

    /// <summary>
    /// Экран склада целиком: затемнение, панель-амбар (`panel-sklad.png`),
    /// вкладки, утопленное поле (`pole-sklad.png`), сетка товаров без ячеек,
    /// строка ёмкости и кнопка расширения. Второй заход по заказу: первая
    /// версия строилась на `panel.png` с лентой (тот же материал, что и
    /// карточка цели) — заказчик её забраковал вместе с карточками ячеек,
    /// прислал три новых элемента (`panel-sklad`, `pole-sklad`,
    /// `vkladka-aktivnaya`) и референс устройства — амбар Township
    /// (`raw/Playrix/referens-sklad.png`). Лента `panel.png` здесь больше не
    /// используется вовсе.
    ///
    /// Строится ПОСЛЕ карточки цели/маяка/карточки имени, но ДО верхнего
    /// HUD-бара — см. комментарий в `Build()` про порядок отрисовки HUD
    /// поверх экрана (счетчики остаются видны и кликабельны поверх
    /// затемнения и панели).
    /// </summary>
    static MarsColony.Game.EkranSklada PostroitEkranSklada(Transform root)
    {
        const float W = RefW, H = RefH;
        var ekran = Uzel(root, "ekran-sklada", 0, 0, W, H, Ugol.Rastyanut, prozrachny: true);

        // Затемнение на весь холст, включая зону под HUD (раздел 1 спеки) —
        // тап по нему закрывает экран, но не открывает (Zakryt, не
        // Perekluchit: сам scrim никогда не должен быть входом в экран).
        //
        // Альфа 0.72, не 0.45: канвас в этом проекте Linear, и uGUI блендит
        // scrim поверх мира В ЛИНЕЙНОМ свете, а кадр в PNG уходит уже через
        // sRGB-гамму (см. `Snyat()`). Линейное затемнение 0.45 на выходе из
        // гаммы дает на кадре только ~22% реального падения яркости
        // (0.55^(1/2.2)~0.77) — глазом это читается как "фон почти не
        // потемнел", хотя альфа честно применилась. Приемка меряет ИМЕННО
        // кадр (не буфер), порог падения 35% на области вне панели — этому
        // порогу нужна линейная альфа около 0.62 и выше, взято 0.72 с
        // запасом. Живой замер после сборки: см. отчет витка.
        var scrim = Uzel(ekran.transform, "zatemnenie", 0, 0, W, H, Ugol.Rastyanut, prozrachny: false);
        var si = scrim.GetComponent<Image>();
        si.sprite = ZalivkaSprite((int)W, (int)H, 0, new Color(0f, 0f, 0f, 0.72f), "sklad-scrim");
        si.color = Color.white; // альфа уже запечена в текстуру, см. комментарий у Vizor()/VizorFon
        scrim.AddComponent<Button>();

        // --- Панель-амбар -----------------------------------------------
        // Размер посчитан так, чтобы 3 полных ряда сетки (17 товаров, 6
        // колонок) влезли в утопленное поле без обрезки и без скролла (см.
        // расчет сетки ниже) — не взято "как в референсе" на глаз, а
        // выведено из содержимого. panelY — НЕ центрирование по высоте:
        // 120, тот же отступ, что был в прежней версии панели, — держит
        // верх панели ниже полосы счетчиков (0..110), которую меряет
        // приемка затемнения; первая попытка центрировала панель по высоте
        // (panelY=55) и верх панели заехал в эту полосу, испортив замер
        // затемнения посторонней непрозрачной подложкой (см. отчет витка).
        const float panelW = 980f, panelH = 760f;
        const float panelX = (RefW - panelW) / 2f, panelY = 120f;

        // Тот же прием масштаба 9-slice, что в KartochkaTseliGen: k = ширина
        // исходника / целевая ширина. panel-sklad.png — 1042x797.
        float kPanel = 1042f / panelW;

        var panelTen = Uzel(ekran.transform, "sklad-panel-ten", panelX - 12, -(panelY - 12 - 6), panelW + 24, panelH + 24,
                            Ugol.SverhuSleva, prozrachny: false);
        var pti = panelTen.GetComponent<Image>();
        pti.sprite = TenSprite(panelW, panelH, 24);
        pti.color = new Color(TenC.r, TenC.g, TenC.b, 0.45f);

        var panel = Uzel(ekran.transform, "sklad-panel", panelX, -panelY, panelW, panelH, Ugol.SverhuSleva, prozrachny: false);
        var pimg = panel.GetComponent<Image>();
        pimg.sprite = SpriteElementa("panel-sklad", new Vector4(60, 60, 60, 140));
        pimg.type = Image.Type.Sliced;
        pimg.pixelsPerUnitMultiplier = kPanel;
        pimg.color = Color.white;

        var korichnevy = Hex("5B3B1E"); // заголовок и текст в теле панели

        var zagolovok = Uzel(panel.transform, "zagolovok", 0, -30, panelW, 44, Ugol.SverhuTsentr, prozrachny: true);
        Nadpis(zagolovok, "СКЛАД", 34, TextAnchor.MiddleCenter, korichnevy);
        var podzagolovok = Uzel(panel.transform, "podzagolovok", 0, -66, panelW, 28, Ugol.SverhuTsentr, prozrachny: true);
        Nadpis(podzagolovok, "Нажмите на товар, чтобы продать", 20, TextAnchor.MiddleCenter, Hex("7A5A3A"));

        // Крестик закрытия — правый верхний угол панели, центр на кромке
        // (заказ явно: половина внутри рамки). panel-sklad.png — простой
        // скругленный прямоугольник без ленты (радиус угла ~44px на этом
        // масштабе), поэтому сдвиг от буквального угла бокса небольшой —
        // в отличие от прежней панели с лентой, где такой же расчет "в лоб"
        // унес крест за пределы панели на кадре приемки (см. отчет витка,
        // урок про то, что борт 9-slice != видимый скругленный угол).
        const float krestHitbox = 88f;
        var krest = Uzel(panel.transform, "krest-zakrytiya", -24, -24, krestHitbox, krestHitbox,
                         Ugol.VerhSprava, prozrachny: false);
        krest.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
        var krestIkonka = Uzel(krest.transform, "ikonka", 0, 0, 64, 64, Ugol.Tsentr, prozrachny: false);
        var ki = krestIkonka.GetComponent<Image>();
        ki.sprite = SpriteElementa("knopka-krest-2", Vector4.zero);
        ki.type = Image.Type.Simple; ki.preserveAspect = true; ki.color = Color.white;
        krest.AddComponent<Button>();

        // --- Вкладки -------------------------------------------------------
        // Ряд из 2 язычков (vkladka-aktivnaya.png, 845x850 — скругленный
        // верх, плоский низ) прямо над полем, низ вкладки касается верха
        // поля. Неактивная вкладка — свой спрайт заказчика
        // vkladka-neaktivnaya.png (808x776), без тонировки.
        const float padX = 40f;          // общий боковой отступ панели: вкладки и поле выровнены по нему
        const float headerH = 80f, gapHeaderTabs = 4f, tabH = 64f;
        float poleTop = headerH + gapHeaderTabs + tabH; // 148 — верх поля от верха панели

        const float tabW = 190f, tabGap = 8f;
        // P6 (АД зона 3): уступ 14px — неактивная становится ниже по высоте (не активная растёт), нижний шов с полем у обеих один и тот же (оба считаются от poleTop своей высотой).
        const float tabHNeaktivnaya = tabH - 14f;
        float kTab = 845f / tabW;

        // Неактивная вкладка — свой спрайт заказчика (vkladka-neaktivnaya.png,
        // 808x776), не тонировка активного; k считается от ширины ИМЕННО
        // этого источника, а не от 845 (ширина vkladka-aktivnaya) — иначе
        // 9-slice растянул бы борт неправильной пропорцией. Строится
        // ПЕРВОЙ, нижним слоем — активная строится после и рисуется поверх (P6).
        var tabModuli = Uzel(panel.transform, "vkladka-moduli", padX + tabW + tabGap, -(poleTop - tabHNeaktivnaya), tabW, tabHNeaktivnaya,
                             Ugol.SverhuSleva, prozrachny: false);
        var tmi = tabModuli.GetComponent<Image>();
        float kTabNeaktivnaya = 808f / tabW;
        tmi.sprite = SpriteElementa("vkladka-neaktivnaya", new Vector4(140, 20, 140, 160));
        tmi.type = Image.Type.Sliced; tmi.pixelsPerUnitMultiplier = kTabNeaktivnaya; tmi.color = Color.white;
        var tabModuliTekst = Uzel(tabModuli.transform, "tekst", 0, -4, tabW, tabHNeaktivnaya - 8, Ugol.Tsentr, prozrachny: true);
        Nadpis(tabModuliTekst, "МОДУЛИ", 22, TextAnchor.MiddleCenter, Hex("8A7256"));

        var tabTovary = Uzel(panel.transform, "vkladka-tovary", padX, -(poleTop - tabH), tabW, tabH,
                             Ugol.SverhuSleva, prozrachny: false);
        var tti = tabTovary.GetComponent<Image>();
        tti.sprite = SpriteElementa("vkladka-aktivnaya", new Vector4(140, 20, 140, 160));
        tti.type = Image.Type.Sliced; tti.pixelsPerUnitMultiplier = kTab; tti.color = Color.white;
        var tabTovaryTekst = Uzel(tabTovary.transform, "tekst", 0, -4, tabW, tabH - 8, Ugol.Tsentr, prozrachny: true);
        Nadpis(tabTovaryTekst, "ТОВАРЫ", 22, TextAnchor.MiddleCenter, korichnevy);

        // --- Поле (утопленная зона под сетку) -------------------------------
        float poleW = panelW - padX * 2f; // 900
        // Дно поля посчитано снизу вверх от нижней панели-бара, чтобы
        // отступ до нижней кромки панели был гарантирован числом, а не
        // остатком. P8 (АД зона 3): было 16, кнопка стояла от кромки 10-14px на кадре —
        // стало 32; капсула емкости считается от той же константы ниже и поднялась на
        // ту же величину (+16) автоматически; poleH в той же формуле укорачивается
        // снизу на те же 16px, чтобы поле не наехало на поднятую кнопку/капсулу.
        // above(32) + бар(70) + below(24) = 126.
        const float barAbove = 32f, barH = 70f, barBelow = 24f;
        float poleH = panelH - poleTop - (barAbove + barH + barBelow); // 790-160-126=504

        float kPole = 1028f / poleW;
        var pole = Uzel(panel.transform, "pole-sklad", padX, -poleTop, poleW, poleH, Ugol.SverhuSleva, prozrachny: false);
        var poi = pole.GetComponent<Image>();
        poi.sprite = SpriteElementa("pole-sklad", new Vector4(40, 40, 40, 40));
        poi.type = Image.Type.Sliced; poi.pixelsPerUnitMultiplier = kPole; poi.color = Color.white;

        // --- Сетка товаров прямо на поле, без карточек-ячеек ----------------
        // pole-sklad.png несет собственную рамку-кант ~40px исходника —
        // "внутренняя" плоская зона поля инсетится от габарита поля на эту
        // рамку, отмасштабированную тем же kPole, иначе первая строка сетки
        // легла бы на скошенный кант, а не на плоское дно углубления.
        float poleInset = 40f / kPole;
        const int cols = 6, colStep = 140, rowStep = 150, ikonkaPx = 84;
        int rows = Mathf.CeilToInt(TOVARY_SKLADA.Length / (float)cols);
        float gridW = (cols - 1) * colStep + ikonkaPx;
        const float chisloH = 4f + 30f; // зазор под иконкой + строка числа
        float itemH = ikonkaPx + chisloH;
        float gridH = (rows - 1) * rowStep + itemH;

        float innerW = poleW - poleInset * 2f, innerH = poleH - poleInset * 2f;
        float gridOffX = poleInset + (innerW - gridW) * 0.5f;
        float gridOffY = poleInset + (innerH - gridH) * 0.5f;

        // Виток UI-26: рантайм-состояние продажи собирается параллельно узлам
        // — по одной записи `SkladSostoyanie.Tovar` на карточку, ссылки на
        // иконку/число берутся из `TovarNaPole` через out, а не повторным
        // поиском по имени (см. договор `ZhivoyInterfeys.Sobrat` — тот же
        // проект уже наступал на "не нашли узел, молча не обновилось").
        //
        // Цена и русское имя — НЕ вторая переписанная от руки константа
        // рядом с TOVARY_SKLADA: в проекте уже есть живой C#-порт домена
        // (`MarsColony.Domain.Config.Goods`, зеркало `goods.ts`, см. докстринг
        // класса), и редактор в состоянии прочитать его прямо сейчас — читать
        // число из TS второй раз руками было бы третьей копией одного и того
        // же числа в проекте, ровно то, от чего уже один раз чинили ёмкость
        // склада (см. комментарий у WAREHOUSE_WARN_RATIO в spec-sklad.md).
        var runtimeTovary = new MarsColony.Game.SkladSostoyanie.Tovar[TOVARY_SKLADA.Length];
        for (int i = 0; i < TOVARY_SKLADA.Length; i++)
        {
            int col = i % cols, row = i / cols;
            float ix = gridOffX + col * colStep;
            float iy = -(gridOffY + row * rowStep);
            TovarNaPole(pole.transform, "tovar-" + TOVARY_SKLADA[i].id, ix, iy, colStep, itemH,
                       ikonkaPx, TOVARY_SKLADA[i], out Image ikonkaImg, out Text chisloTxt);

            Good dobro = Goods.Of(TOVARY_SKLADA[i].id);
            runtimeTovary[i] = new MarsColony.Game.SkladSostoyanie.Tovar
            {
                id = TOVARY_SKLADA[i].id,
                nazvanie = dobro.name,
                price = dobro.price,
                qty = TOVARY_SKLADA[i].qty,
                row = row,
                ikonka = ikonkaImg,
                chislo = chisloTxt,
            };
        }

        // --- Нижняя полоса: ёмкость слева, кнопка "РАСШИРИТЬ" справа --------
        int summa = 0;
        foreach (var t in TOVARY_SKLADA) summa += t.qty;
        // Капсула-подложка заказчика (kapsula-emkost.png, 1281x497) — border
        // и pixelsPerUnitMultiplier считаются от ВЫСОТЫ, не от ширины: пилюля
        // растянута сильно по горизонтали, а торцевые скругления (борт L/R)
        // держатся высотой источника, как у остальных горизонтальных капсул
        // проекта (см. kapsula-a/b по той же схеме выше).
        const float kapsW = 300f, kapsH = 56f;
        float kKapsula = 497f / kapsH;
        var emkostKapsula = Uzel(panel.transform, "emkost-kapsula", padX, barAbove + (barH - kapsH) / 2f,
                                 kapsW, kapsH, Ugol.SnizuSleva, prozrachny: false);
        var eki = emkostKapsula.GetComponent<Image>();
        eki.sprite = SpriteElementa("kapsula-emkost", new Vector4(200, 120, 200, 120));
        eki.type = Image.Type.Sliced; eki.pixelsPerUnitMultiplier = kKapsula; eki.color = Color.white;
        var emkost = Uzel(emkostKapsula.transform, "tekst", 0, 0, kapsW, kapsH, Ugol.Tsentr, prozrachny: true);
        Nadpis(emkost, $"Ёмкость: {summa} / {SKLAD_EMKOST}", 26, TextAnchor.MiddleCenter, korichnevy);
        var emkostTxt = emkost.GetComponent<Text>();

        const float knopkaW = 300f, knopkaH = 70f;
        var knopkaRash = Uzel(panel.transform, "knopka-rasshirit", padX + 4f, barAbove + 8f, knopkaW, knopkaH,   // P8: inspektor UI-34 nameril 25 px ot nizhney kromki paneli (ploskaya chast), tsel 30-35 -> +8
                              Ugol.SnizuSprava, prozrachny: false);
        var kri = knopkaRash.GetComponent<Image>();
        kri.sprite = SpriteElementa("knopka-a", new Vector4(220, 80, 220, 80));
        kri.type = Image.Type.Sliced; kri.pixelsPerUnitMultiplier = 450f / knopkaH; kri.color = Color.white;
        knopkaRash.AddComponent<Button>(); // функциональность пустая — заказ явно откладывает ее
        var knopkaRashTekst = Uzel(knopkaRash.transform, "tekst", 0, 4, knopkaW, 44, Ugol.Tsentr, prozrachny: true);
        Nadpis(knopkaRashTekst, "РАСШИРИТЬ", 28, TextAnchor.MiddleCenter, Color.white);
        var obvRash = knopkaRashTekst.AddComponent<Outline>();
        obvRash.effectColor = CtaTekstObvodka; obvRash.effectDistance = new Vector2(2f, -2f);
        var tenRashTeksta = knopkaRashTekst.AddComponent<Shadow>();
        tenRashTeksta.effectColor = new Color(0f, 0f, 0f, 0.5f); tenRashTeksta.effectDistance = new Vector2(0f, -2f);

        // --- Попап-степпер продажи и тултип "пусто" (виток UI-26) ----------
        // Оба — одна переиспользуемая копия на всю сетку (не по одной на
        // карточку): `SkladSostoyanie` переставляет их к нужному товару, а
        // не плодит 17 скрытых попапов.
        PostroitPopupProdazhi(panel.transform, panelW, panelH,
            out GameObject popupRoot, out RectTransform popupTelo, out Text popupNazvanie,
            out Text popupChislo, out Text popupProdatTekst, out Button popupMinus, out Button popupPlus,
            out Button popupProdat, out Button popupZatemnenie);
        PostroitTooltipSklada(panel.transform, out GameObject tultip, out Text tultipTxt);

        var sostoyanie = ekran.AddComponent<MarsColony.Game.SkladSostoyanie>();
        sostoyanie.tovary = runtimeTovary;
        sostoyanie.emkost = summa;
        sostoyanie.emkostCap = SKLAD_EMKOST;
        sostoyanie.emkostTekst = emkostTxt;
        sostoyanie.popup = popupRoot;
        sostoyanie.popupTelo = popupTelo;
        sostoyanie.popupNazvanie = popupNazvanie;
        sostoyanie.popupChislo = popupChislo;
        sostoyanie.popupProdatTekst = popupProdatTekst;
        sostoyanie.tultip = tultip;
        sostoyanie.tultipTekst = tultipTxt;
        // P7: поле симметрично относительно центра панели (padX слева и справа одинаков),
        // а popup-узлы растянут точно на панель и делят тот же центр — половина ширины поля
        // минус отступ 12px — готовый лимит для клэмпа в PozicionirovatPopup.
        sostoyanie.poleKlampPolovina = poleW / 2f - 12f;

        for (int i = 0; i < runtimeTovary.Length; i++)
        {
            Button btn = pole.transform.Find("tovar-" + runtimeTovary[i].id)?.GetComponent<Button>();
            if (btn != null)
                UnityEditor.Events.UnityEventTools.AddIntPersistentListener(btn.onClick, sostoyanie.Tap, i);
        }
        UnityEditor.Events.UnityEventTools.AddPersistentListener(popupMinus.onClick, sostoyanie.StepperMinus);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(popupPlus.onClick, sostoyanie.StepperPlus);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(popupProdat.onClick, sostoyanie.Prodat);
        // Крестика внутри попапа нет (инспектор, спека 2.6: "отдельного крестика
        // нет") — закрытие только тапом по своему затемнению, тот же Zakryt.
        UnityEditor.Events.UnityEventTools.AddPersistentListener(popupZatemnenie.onClick, sostoyanie.ZakrytPopup);

        var komponent = ekran.AddComponent<MarsColony.Game.EkranSklada>();
        // Persistent-слушатели (см. комментарий у KnopkaMenyu-привязки в
        // Build()) — закрытие и крестиком, и тапом по затемнению, оба через
        // Zakryt (не Perekluchit): ни один из этих двух входов не должен
        // ОТКРЫВАТЬ экран повторным тапом.
        UnityEditor.Events.UnityEventTools.AddPersistentListener(scrim.GetComponent<Button>().onClick, komponent.Zakryt);
        UnityEditor.Events.UnityEventTools.AddPersistentListener(krest.GetComponent<Button>().onClick, komponent.Zakryt);

        ekran.SetActive(false);
        return komponent;
    }

    /// <summary>
    /// Попап-степпер продажи (виток UI-26, инспекторская правка по
    /// `spec-sklad.md` разделу 2.6: компактный пузырь ~280x168, якорится к
    /// нажатой карточке — не центральная модалка на всю сетку, как было в
    /// первой сдаче). Тело — `pole-sklad` Sliced, тот же материал, что и
    /// раньше. Своего крестика нет (спека: "отдельного крестика нет") —
    /// закрытие только тапом вне пузыря, через собственное затемнение.
    ///
    /// Иконки товара внутри НЕТ — раздел 2.6 спеки её не описывает вовсе
    /// (заголовок, степпер, кнопка подтверждения), первая сдача добавляла
    /// иконку от себя, инспектор её не просил и места на пузыре 280x168 под
    /// неё все равно нет.
    ///
    /// Хитбоксы степпера — 64x64, не 88x88 (раздел 4 спеки просит 88, но при
    /// высоте пузыря 168 два хитбокса 88 и кнопка подтверждения физически не
    /// умещаются без наезда друг на друга — тело пришлось бы снова раздувать,
    /// а именно раздутое тело инспектор и забраковал в прошлый раз). Явный
    /// компромисс, отмечен здесь, а не молчком.
    ///
    /// Один переиспользуемый узел на всю сетку — `SkladSostoyanie` переносит
    /// (`popupTelo.anchoredPosition`) его к нужной карточке при каждом тапе.
    /// </summary>
    static void PostroitPopupProdazhi(Transform panel, float panelW, float panelH,
        out GameObject popupRoot, out RectTransform telo, out Text nazvanie, out Text chislo, out Text prodatTekst,
        out Button minus, out Button plus, out Button prodat, out Button zatemnenieBtn)
    {
        var korichnevy = Hex("5B3B1E");

        popupRoot = Uzel(panel, "popup-sklada", 0, 0, panelW, panelH, Ugol.Rastyanut, prozrachny: true);

        // Альфа 0.25, не 0.45: пузырь маленький и сидит рядом с карточкой,
        // остальная сетка должна оставаться читаемой под лёгкой дымкой, а не
        // тонуть в ней, как было под затемнением полноэкранной модалки.
        var zatemnenie = Uzel(popupRoot.transform, "popup-zatemnenie", 0, 0, panelW, panelH, Ugol.Rastyanut, prozrachny: false);
        var zi = zatemnenie.GetComponent<Image>();
        zi.sprite = ZalivkaSprite((int)panelW, (int)panelH, 0, new Color(0f, 0f, 0f, 0.25f), "sklad-popup-zatemnenie");
        zi.color = Color.white;
        zatemnenieBtn = zatemnenie.AddComponent<Button>();

        const float teloW = 280f, teloH = 168f;
        // P7 (АД зона 3, критик): пузырь отрывается от сетки собственной мягкой тенью,
        // а не только затемнением попапа. teloGo стал прозрачной рамкой-держателем
        // позиции (её двигает SkladSostoyanie.PozicionirovatPopup как и раньше), тело и тень —
        // её дети: тень построена ПЕРВОЙ и поэтому лежит ниже тела по отрисовке.
        var teloGo = Uzel(popupRoot.transform, "popup-telo", 0, 0, teloW, teloH, Ugol.Tsentr, prozrachny: true);
        telo = teloGo.GetComponent<RectTransform>();

        var teloTen = Uzel(teloGo.transform, "popup-telo-ten", 0, -6, teloW, teloH, Ugol.Tsentr, prozrachny: false);
        var tteni = teloTen.GetComponent<Image>();
        tteni.sprite = TenSprite(teloW, teloH, 16);
        tteni.color = new Color(TenC.r, TenC.g, TenC.b, 0.35f);

        var teloBody = Uzel(teloGo.transform, "popup-telo-body", 0, 0, teloW, teloH, Ugol.Tsentr, prozrachny: false);
        var ti = teloBody.GetComponent<Image>();
        ti.sprite = SpriteElementa("pole-sklad", new Vector4(40, 40, 40, 40));
        ti.type = Image.Type.Sliced; ti.pixelsPerUnitMultiplier = 1028f / teloW; ti.color = Color.white;

        var nazvanieUz = Uzel(teloGo.transform, "popup-nazvanie", 0, -14, teloW - 24, 26, Ugol.SverhuTsentr, prozrachny: true);
        Nadpis(nazvanieUz, "", 24, TextAnchor.MiddleCenter, korichnevy);
        nazvanie = nazvanieUz.GetComponent<Text>();

        // --- Степпер: [-] N [+] ---------------------------------------------
        var minusUz = Uzel(teloGo.transform, "popup-minus", -78, -50, 64, 64, Ugol.SverhuTsentr, prozrachny: false);
        var mi = minusUz.GetComponent<Image>();
        mi.sprite = FormaSprite(Forma.Krug, 64); mi.color = Hex("C9A876");
        minus = minusUz.AddComponent<Button>();
        var minusTekst = Uzel(minusUz.transform, "tekst", 0, 2, 64, 64, Ugol.Tsentr, prozrachny: true);
        Nadpis(minusTekst, "-", 32, TextAnchor.MiddleCenter, Color.white);

        var plusUz = Uzel(teloGo.transform, "popup-plus", 78, -50, 64, 64, Ugol.SverhuTsentr, prozrachny: false);
        var pli = plusUz.GetComponent<Image>();
        pli.sprite = FormaSprite(Forma.Krug, 64); pli.color = Hex("C9A876");
        plus = plusUz.AddComponent<Button>();
        var plusTekst = Uzel(plusUz.transform, "tekst", 0, 2, 64, 64, Ugol.Tsentr, prozrachny: true);
        Nadpis(plusTekst, "+", 32, TextAnchor.MiddleCenter, Color.white);

        var chisloUz = Uzel(teloGo.transform, "popup-chislo", 0, -50, 84, 64, Ugol.SverhuTsentr, prozrachny: true);
        Nadpis(chisloUz, "1", 30, TextAnchor.MiddleCenter, korichnevy, mono: true);
        chislo = chisloUz.GetComponent<Text>();

        // --- Кнопка подтверждения: монета + "Продать за {sum} кр." --------
        // Одна строка вместо заголовка + отдельной "Итого" — сумма живёт
        // прямо в тексте кнопки (спека 2.6).
        const float prodatW = 200f, prodatH = 40f;
        var prodatUz = Uzel(teloGo.transform, "popup-prodat", 0, -124, prodatW, prodatH, Ugol.SverhuTsentr, prozrachny: false);
        var pi = prodatUz.GetComponent<Image>();
        pi.sprite = SpriteElementa("knopka-a", new Vector4(220, 80, 220, 80));
        pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 450f / prodatH; pi.color = Color.white;
        prodat = prodatUz.AddComponent<Button>();

        var monetaUz = Uzel(prodatUz.transform, "moneta", 16, 0, 28, 28, Ugol.SlevaTsentr, prozrachny: false);
        var moi = monetaUz.GetComponent<Image>();
        var moneta = SpriteResursa("znachok-kredity");
        if (moneta != null) moi.sprite = moneta;
        moi.preserveAspect = true; moi.color = Color.white;

        var prodatTekstUz = Uzel(prodatUz.transform, "tekst", 52, 0, prodatW - 68, prodatH, Ugol.Sleva, prozrachny: true);
        Nadpis(prodatTekstUz, "Продать", 22, TextAnchor.MiddleLeft, Color.white);
        prodatTekst = prodatTekstUz.GetComponent<Text>();
        var obvProdat = prodatTekstUz.AddComponent<Outline>();
        obvProdat.effectColor = CtaTekstObvodka; obvProdat.effectDistance = new Vector2(2f, -2f);
        var tenProdatTeksta = prodatTekstUz.AddComponent<Shadow>();
        tenProdatTeksta.effectColor = new Color(0f, 0f, 0f, 0.5f); tenProdatTeksta.effectDistance = new Vector2(0f, -2f);

        popupRoot.SetActive(false);
    }

    /// <summary>
    /// Тултип "Пока нет на складе" (виток UI-26) — тап по нулевой позиции без
    /// попапа, живёт 1.5с (`SkladSostoyanie`). Капсула — симметричный вариант
    /// без дыры под медальон (`PlashkaImeniSprite`, тот же прием, что у
    /// карточки имени), тултипу медальон не нужен вовсе.
    /// </summary>
    static void PostroitTooltipSklada(Transform panel, out GameObject tultip, out Text tultipTxt)
    {
        const float h = 52f, w = 320f, srcH = 403f;
        tultip = Uzel(panel, "sklad-tultip", 0, 0, w, h, Ugol.SverhuTsentr, prozrachny: false);
        var img = tultip.GetComponent<Image>();
        var plashka = PlashkaImeniSprite();
        if (plashka != null)
        {
            img.sprite = plashka; img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = srcH / h; img.color = Color.white;
        }
        var tekst = Uzel(tultip.transform, "tekst", 0, 0, w - 24, h, Ugol.Tsentr, prozrachny: true);
        Nadpis(tekst, "Пока нет на складе", 24, TextAnchor.MiddleCenter, Color.white);
        tultipTxt = tekst.GetComponent<Text>();
        tultip.SetActive(false);
    }

    /// <summary>
    /// Одна позиция сетки склада ПРЯМО на поле, без карточки-ячейки под ней
    /// (второй заход по заказу отказался от `yacheyka.png` целиком — заказчик
    /// забраковал ячейки вместе с прежней панелью). Иконка и число садятся
    /// прямо на утопленное поле `pole-sklad.png`, как в референсе устройства
    /// (`raw/Playrix/referens-sklad.png`): предмет плавает на подложке без
    /// собственной рамки.
    ///
    /// Пустая позиция (qty == 0): иконка притушена альфой 0.35, число серым
    /// — тот же язык "разблокировано, но пусто", что был у прежних ячеек,
    /// перенесенный на карточку без рамки.
    /// </summary>
    // Виток UI-26: тап по товару открывает попап-степпер продажи (`SkladSostoyanie`,
    // `spec-sklad.md` раздел 2.6/4) — карточке нужен собственный Button с
    // раскастящейся площадью на весь блок 140x118 (больше требуемых 88x88, отдельный
    // паддинг не нужен, см. спека раздел 4). Раньше узел строился прозрачным
    // (Image удалялся) — под клик подкладывать было нечего, отсюда и была дыра
    // "ворот UI-26: пишет подсказку, а продажи нет". Иконка и число возвращаются
    // вызывающему через out, чтобы `SkladSostoyanie` могло их перекрашивать/менять
    // после продажи без повторного поиска по имени.
    /// <summary>P9: нулевой товар тише заполненного: темнее серый при меньшей альфе и масштаб 0.9; то же значение читает SkladSostoyanie.Prodat.</summary>
    public static readonly Color TsvetPustogoTovara = new Color(0.42f, 0.42f, 0.42f, 0.62f);
    public const float MASSHTAB_PUSTOGO_TOVARA = 0.9f;

    static void TovarNaPole(Transform parent, string imya, float x, float y, float w, float h,
                            float ikonkaPx, TovarSklada t, out Image ikonkaImg, out Text chisloTxt)
    {
        var item = Uzel(parent, imya, x, y, w, h, Ugol.SverhuSleva, prozrachny: false);
        var itemImg = item.GetComponent<Image>();
        itemImg.color = new Color(0f, 0f, 0f, 0f); // невидимая, но raycastTarget=true по умолчанию
        item.AddComponent<Button>();

        bool pusto = t.qty <= 0;
        ikonkaImg = null;
        var spr = SpriteResursa(t.ikonka);
        if (spr != null)
        {
            var ikon = Uzel(item.transform, "ikonka", 0, 0, ikonkaPx, ikonkaPx, Ugol.SverhuTsentr, prozrachny: false);
            var ii = ikon.GetComponent<Image>();
            ii.sprite = spr;
            // Виток "ночь-5" UI-25: альфа 0.35 на белом гасила яркие/бирюзовые
            // предметы (рулон ткани) до полного слияния с полем — инспектор
            // не видел силуэт вовсе. Обесцвечивание (серый тон + меньшая
            // прозрачность, чем раньше) держит форму предмета читаемой на
            // любом исходном цвете иконки, а не только на темных.
            ii.color = pusto ? TsvetPustogoTovara : Color.white;   // P9 (критик): нулевые тише — темнее серый и прозрачнее, контраст к полю держит тёмный тон; 0.5/0.6 давало разницу 31/255 к полю у рулона ткани, порог 40
            if (pusto) ikon.transform.localScale = Vector3.one * MASSHTAB_PUSTOGO_TOVARA;
            ikonkaImg = ii;
        }

        var chislo = Uzel(item.transform, "chislo", 0, -(ikonkaPx + 4f), w, 30, Ugol.SverhuTsentr, prozrachny: true);
        // Было A08C70 — контраст 2.87:1 к полю pole-sklad (245,241,229),
        // ворота UI-25 требуют ≥4.5:1. 8A7256 задан инспектором прямо.
        Nadpis(chislo, t.qty.ToString(), 26, TextAnchor.MiddleCenter,
              pusto ? Hex("8F7A5E") : Hex("5B3B1E"), mono: true);   // P9: ноль светлее (было 7A6247)
        chisloTxt = chislo.GetComponent<Text>();
    }

    // ---- маяк цели на карте (виток UI-10) ----------------------------------

    /// <summary>
    /// Маяк текущей цели: плавающая экранная метка над куполом, на который
    /// указывает <see cref="MarsColony.Game.QuestMarker"/> (см.
    /// <see cref="ObespechitQuestMarker"/>) — тот же источник, что читает
    /// карточка цели ниже. Два независимых критика цикла интерфейса назвали
    /// главной бедой ровно это: карточка говорит "посеять в теплице", а
    /// теплицу игрок ищет глазами по всей сцене.
    ///
    /// Иконка внутри — БУКВАЛЬНО тот же вызов `Ikonka(0)`, что у миниатюры
    /// карточки цели в <see cref="KartochkaTseli"/>: совпадение проверяется
    /// по ссылке на `Sprite`, а не "похоже", и одинаковый вызов — самый
    /// прямой способ этого достичь.
    ///
    /// Форма (капля/пин) и цвет (#FFF6E2, тёплый почти белый — паспорт
    /// onboarding) временные, геометрия на глаз бейджей раздела 2.8
    /// референсов. Финальный вид и движение — не эта задача: точки крепления
    /// (`RectTransform`, ссылка на `Image` иконки, показать/спрятать) отданы
    /// художнику интерфейса и juice в компоненте `MayakTseli`.
    ///
    /// Стартует выключенным и живёт СВОБОДНО на холсте (не ребёнком карточки
    /// цели): позицию каждый кадр ставит `VyborZdaniy` проекцией макушки
    /// купола, а не версткой карточки.
    /// </summary>
    static void PostroitMayak(Transform root)
    {
        const int d = 48;
        // Корень прозрачный: позиция считается по нему (MayakTseli.Rect), а
        // рисуют дети в порядке снизу вверх: тёмная оправа ножки, тёмная
        // оправа круга, светлая ножка, светлый круг, силуэт. Виток UI-11.
        //
        // Оправа `#2A3A46` — та же, что у медальонов счётчиков (2.5 п.3).
        // Инспектор UI-10 видел, как белый скафандр астронавта «перекрывает»
        // край круга: холст ScreenSpaceCamera на planeDistance 1 физически
        // поверх мира, так что это не порядок отрисовки, а слияние белого с
        // почти белым. Тёмный обод отделяет маяк от любого светлого юнита.
        //
        // Ножка — ромб (квадрат под 45°), верхняя половина уходит под круг,
        // нижний кончик выходит на ~10 px ниже круга: при зазоре 12 px он
        // почти касается макушки купола и связывает маяк с целью через любой
        // юнит, вставший в створ.
        var mayak = Uzel(root, "mayak-tseli", 0, 0, d, d, Ugol.Tsentr, prozrachny: true);
        // nozh 14 давал кончик на 7 px ниже круга — до макушки при зазоре 12
        // не доставал (замер: 8 из 12 px отрезка). 20 даёт диагональ 28 и
        // кончик на 11 px ниже круга.
        const int obod = 3, nozh = 20;
        Color opravaC = Hex("2A3A46"), teloC = Hex("FFF6E2");

        var opravaNozhki = Uzel(mayak.transform, "oprava-nozhki", 0, -(d / 2f) + 3, nozh + 2 * obod, nozh + 2 * obod, Ugol.Tsentr, prozrachny: false);
        opravaNozhki.GetComponent<Image>().sprite = Skruglenny(nozh + 2 * obod, nozh + 2 * obod, 2);
        opravaNozhki.GetComponent<Image>().color = opravaC;
        opravaNozhki.GetComponent<RectTransform>().localRotation = Quaternion.Euler(0, 0, 45);

        var oprava = Uzel(mayak.transform, "oprava", 0, 0, d + 2 * obod, d + 2 * obod, Ugol.Tsentr, prozrachny: false);
        oprava.GetComponent<Image>().sprite = FormaSprite(Forma.Krug, d + 2 * obod);
        oprava.GetComponent<Image>().color = opravaC;

        var nozhka = Uzel(mayak.transform, "nozhka", 0, -(d / 2f) + 3, nozh, nozh, Ugol.Tsentr, prozrachny: false);
        nozhka.GetComponent<Image>().sprite = Skruglenny(nozh, nozh, 2);
        nozhka.GetComponent<Image>().color = teloC;
        nozhka.GetComponent<RectTransform>().localRotation = Quaternion.Euler(0, 0, 45);

        var krug = Uzel(mayak.transform, "krug", 0, 0, d, d, Ugol.Tsentr, prozrachny: false);
        krug.GetComponent<Image>().sprite = FormaSprite(Forma.Krug, d);
        krug.GetComponent<Image>().color = teloC;

        // Силуэт теплицы под 32 px вместо детального спрайта карточки: на
        // таком размере тот спрайт схлопывался в зелёное пятно (инспектор
        // UI-10). Полусфера с двумя рёбрами и одной дугой, тёмным по светлому.
        var ikon = Uzel(mayak.transform, "ikonka", 0, 1, d - 16, d - 16, Ugol.Tsentr, prozrachny: false);
        var ikonImg = ikon.GetComponent<Image>();
        ikonImg.sprite = SiluetTeplitsy(64);
        ikonImg.color = opravaC;

        var komponent = mayak.AddComponent<MarsColony.Game.MayakTseli>();
        komponent.Rect = mayak.GetComponent<RectTransform>();
        komponent.Ikonka = ikonImg;

        mayak.SetActive(false);
    }

    // ---- всплывающая карточка имени здания (виток UI-3) --------------------

    /// <summary>
    /// Карточка имени выбранного здания. Раздел 3 интервью цикла UI-3: клик
    /// по зданию заменил ряд подписанных кнопок, и подпись должна появляться
    /// не фиксированной панелью в углу, а всплывать У ЗДАНИЯ.
    ///
    /// Почему не фиксированная панель. Инспектор, смотревший кадр после
    /// снятия кнопок, отметил: низ кадра справа занят плотным песочным
    /// зданием и полем панелей, а карточка цели слева — уже единственная
    /// тяжелая непрозрачная плашка кадра. Вторая такая же в правом углу
    /// закрыла бы весь низ. Всплывающая у объекта карточка не соревнуется с
    /// ними за место и не появляется, когда ничего не выбрано.
    ///
    /// Материал — "визор" (см. `Vizor()`), не кремовый `Korpus()`: тот же
    /// язык, что у счетчиков сверху, а не язык диалоговых окон карточки цели.
    /// Заказчик прямо просил тот же рецепт: темный визор 0.58, белый текст,
    /// волосок #5FE3E8 по верхней грани — `Vizor()` дает все три пункта одним
    /// вызовом, ничего не добавлено поверх.
    ///
    /// Пивот сдвинут на (0.5, 0) ПОСЛЕ создания: `Uzel()` не знает такого
    /// угла, а рантайму нужен именно нижний центр — карточка обязана расти
    /// ВВЕРХ от точки над крышей, а не быть отцентрована на ней (иначе
    /// нижняя половина карточки резала бы силуэт здания).
    ///
    /// ШИРИНА — ПРАВКА ПО ИТОГАМ ПРИЕМКИ ПОПЫТКИ 1 (находка 4). Раньше здесь
    /// была фиксированная ширина 380 px под самую длинную подпись словаря —
    /// на подписи короче она давала 92 px пустого поля с каждой стороны,
    /// визор читался полосой поперек сцены, а лишнее поле как раз и
    /// перекрывало соседний бейдж дефицита питания (находка 2). Ширину под
    /// текст теперь считает рантайм (`ZhivoyInterfeys.PokazatImyaZdaniya`)
    /// каждый показ, здесь только стартовый размер и спрайт, готовый
    /// тянуться. Обычный Single-спрайт при растяжении сплющил бы скругленные
    /// углы — спрайт визора пересоздан как 9-slice (`Image.Type.Sliced`) с
    /// границей чуть шире радиуса скругления: середина тянется, углы и
    /// волоски остаются как нарисованы.
    ///
    /// Стартует выключенной. Показывает, двигает и меняет ширину `VyborZdaniy`
    /// вместе с `ZhivoyInterfeys` — единственные, кто знает, что сейчас выбрано.
    /// </summary>
    static void KartochkaImeni(Transform root)
    {
        const float h = 52f;
        const float srcH = 403f; // высота исходника kapsula-a.png — см. PlashkaImeniSprite
        float w = MarsColony.Game.ZhivoyInterfeys.MIN_SHIRINA_KARTOCHKI;

        // Виток "ночь-2": элемент заказчика (тёмное стекло kapsula-a) вместо
        // процедурного Vizor() — спека раздел 0, требование заказчика на
        // элементы из Assets/UI/Elementy, не примитивы кода.
        var kart = Uzel(root, "kartochka-imeni", 0, 0, w, h, Ugol.Tsentr, prozrachny: false);
        var kartRt = kart.GetComponent<RectTransform>();
        kartRt.pivot = new Vector2(0.5f, 0f);

        var kartImg = kart.GetComponent<Image>();
        var plashka = PlashkaImeniSprite();
        if (plashka != null)
        {
            kartImg.sprite = plashka;
            kartImg.type = Image.Type.Sliced;
            kartImg.pixelsPerUnitMultiplier = srcH / h;
            kartImg.color = Color.white;
        }
        else
        {
            // Явный, видимый откат вместо тихой дыры: если художник еще не
            // положил kapsula-a.png, карточка остается тёмным визором, а не
            // невидимым прямоугольником.
            Debug.LogWarning("[interfeys] kapsula-a.png не найден — карточка имени на запасном Vizor()");
            kartImg.enabled = false; // свой Image молчит белым квадратом — фон дает дочерний визор
            var zapasnoy = Vizor(kart.transform, "vizor-zapasnoy", 0, 0, w, h, 14, Ugol.Tsentr);
            zapasnoy.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        }

        // P10 (АД F1/F2): хвостик к макушке — треугольник под низом плашки цвета её нижней кромки (34,29,6 — замер play_tap_kupol.png).
        // Пивот плашки (0.5, 0) стоит на макушке + 12 px, хвостик 10 px вниз — его остриё в 2 px над макушкой; верхний пиксель входит в кромку, шва нет.
        var hvostSpr = SpriteNaPuti("Assets/UI/Generated/hvostik-plashki.png", Vector4.zero);
        if (hvostSpr != null)
        {
            var hvost = Uzel(kart.transform, "hvostik", 0, -9f, 20f, 10f, Ugol.SnizuTsentr, prozrachny: false);
            var hvi = hvost.GetComponent<Image>();
            hvi.sprite = hvostSpr; hvi.type = Image.Type.Simple; hvi.color = Color.white; hvi.raycastTarget = false;
        }

        // Текст — фиксированной щедрой ширины с переполнением: центрируется
        // в панели независимо от ее итоговой ширины. Реальную ширину для
        // расчета панели рантайм читает через `Text.preferredWidth`, а не
        // через размер этого узла — размер узла ни на что не влияет.
        //
        // Кегль 26, не 24: правило роли требует минимум 26px на карточке
        // (спека раздел 0/2.2). Белый текст с тёмной обводкой — стекло тёмное
        // само по себе, но обводка страхует контраст на светлых участках
        // блика стекла, как и у остальных подписей поверх тёмных корпусов
        // этого файла (см. Skoba/CTA).
        var tekst = Uzel(kart.transform, "imya", 0, 0, 400, h, Ugol.Tsentr, prozrachny: true);
        Nadpis(tekst, "", 26, TextAnchor.MiddleCenter, Color.white);
        var obvodkaImeni = tekst.AddComponent<Outline>();
        obvodkaImeni.effectColor = new Color(0.06f, 0.10f, 0.14f, 0.85f); // #101A24-подобный, темнее самого стекла
        obvodkaImeni.effectDistance = new Vector2(1.5f, -1.5f);

        kart.SetActive(false);
    }

    /// <summary>
    /// Пиктограмма кнопки: снимок настоящей модели, если он есть, иначе прежняя
    /// процедурная. Снимок предпочтителен потому, что кнопка обязана показывать
    /// ровно ту постройку, которая появится в мире.
    /// </summary>
    static Sprite Ikonka(int i)
    {
        string[] snimki = { "ikonka-kupol-gidroponiki", "ikonka-pishchevoy-modul",
                            "ikonka-atmosferny-modul", "ikonka-tekstilny-modul", "ikonka-sklad" };
        var s = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Ikonki/" + snimki[i] + ".png");
        if (s != null) return s;
        return Zagruzit("Assets/UI/Generated/knopka-ikonka-" + i + ".png");
    }

    static void Skoba(Transform p, string imya, float x)
    {
        var s = Uzel(p, imya, x, 0, 16, 24, Ugol.SlevaTsentr, prozrachny: false);
        var im = s.GetComponent<Image>();
        im.sprite = Zagruzit("Assets/UI/Generated/skoba.png");
        // Виток UI-7, попытка 1: было C79A4E (золотисто-коричневый теплый),
        // затем перекрашено в "нейтральный металл" #3D5A68 — инспектор
        // attempt-2 отклонил именно эту правку: контраст к #101A24 падал в
        // разы, два тусклых серых пятна на грани нечитаемости. Попытка 2:
        // холодный акцент семьи кант-волоска карточки (KartochkaTseliKant,
        // #5FE3E8) вместо нейтрального металла — деталь, а не пятно.
        im.color = Hex("5FE3E8");
    }

    // ---- низ справа: хаб --------------------------------------------------

    static void Hab(Transform root)
    {
        // Полосы-подложки нет: кнопки лежат отдельными корпусами. Это одним
        // движением снимает двойную обводку и сбитую центровку прежнего хаба.
        // Подписи в одно слово. Канонические двухсловные имена не влезают в 116:
        // перенос рвал их посреди слова («Атмосферн / ый модуль»), а это хуже
        // любого сокращения. Слово-носитель домена читается и одно.
        string[] imena = { "Теплица", "Пищевой", "Атмосфера", "Текстиль", "Склад" };
        for (int i = 0; i < 5; i++)
        {
            float xSprava = 24 + (4 - i) * 130;
            var kn = Korpus(root, "knopka-" + i, xSprava, 100, 116, 116, 26,
                            KremVerh, KremNiz, 20, PolkaC, Ugol.SnizuSprava, ten: 0.32f,
                            kontur: KonturKn, konturPx: 3);
            kn.AddComponent<Button>();

            var ik = Uzel(kn.transform, "ikonka", 0, -4, 54, 54, Ugol.SverhuTsentr, prozrachny: false);
            var ii = ik.GetComponent<Image>();
            ii.sprite = Ikonka(i);
            ii.color = Color.white;

            // Подпись внутри кнопки на креме, а не под кнопкой поверх мира:
            // кегль 18 белым на пестром грунте это прописная 1.0 мм, то есть грязь.
            //
            // Кегль 17, а не 20 из спеки, и перенос по словам: «Атмосферный» и
            // «Текстильный» при 20 не влезают в 116 и вылезали за корпус кнопки
            // на соседнюю. Спека считала кегль, не проверив самое длинное слово.
            var po = Uzel(kn.transform, "podpis", 0, 24, 110, 26, Ugol.SnizuTsentr, prozrachny: true);
            Nadpis(po, imena[i], 18, TextAnchor.LowerCenter, TekstC);
            var pt2 = po.GetComponent<Text>();
            pt2.lineSpacing = 0.88f;
            pt2.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }

    // ---- примитивы --------------------------------------------------------

    enum Ugol { VerhSleva, VerhSprava, SverhuSleva, SverhuTsentr, SnizuSleva, SnizuSprava, SnizuTsentr,
                Sleva, Sprava, Tsentr, SlevaTsentr, Rastyanut }
    enum Forma { Krug, Zvezda, Shestigrannik }

    static GameObject Uzel(Transform parent, string imya, float x, float y, float w, float h,
                           Ugol u, bool prozrachny = false)
    {
        var go = new GameObject(imya, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(w, h);

        Vector2 amin, amax, piv, pos = new Vector2(x, y);
        switch (u)
        {
            case Ugol.VerhSleva:    amin = amax = new Vector2(0, 1);    piv = new Vector2(0, 1);    pos = new Vector2(x, -y); break;
            case Ugol.VerhSprava:   amin = amax = new Vector2(1, 1);    piv = new Vector2(1, 1);    pos = new Vector2(-x, -y); break;
            case Ugol.SverhuSleva:  amin = amax = new Vector2(0, 1);    piv = new Vector2(0, 1);    break;
            case Ugol.SverhuTsentr: amin = amax = new Vector2(0.5f, 1); piv = new Vector2(0.5f, 1); break;
            case Ugol.SnizuSleva:   amin = amax = new Vector2(0, 0);    piv = new Vector2(0, 0);    break;
            case Ugol.SnizuSprava:  amin = amax = new Vector2(1, 0);    piv = new Vector2(1, 0);    pos = new Vector2(-x, y); break;
            case Ugol.SnizuTsentr:  amin = amax = new Vector2(0.5f, 0); piv = new Vector2(0.5f, 0); break;
            case Ugol.Sleva:        amin = amax = new Vector2(0, 0.5f); piv = new Vector2(0, 0.5f); break;
            case Ugol.Sprava:       amin = amax = new Vector2(1, 0.5f); piv = new Vector2(1, 0.5f); pos = new Vector2(x, y); break;
            case Ugol.SlevaTsentr:  amin = amax = new Vector2(0, 0.5f); piv = new Vector2(0, 0.5f); break;
            case Ugol.Rastyanut:
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                if (prozrachny) Object.DestroyImmediate(go.GetComponent<Image>());
                return go;
            default:                amin = amax = piv = new Vector2(0.5f, 0.5f); break;
        }
        rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = piv; rt.anchoredPosition = pos;
        if (prozrachny) Object.DestroyImmediate(go.GetComponent<Image>());
        return go;
    }

    /// <summary>
    /// Корпус целиком: тень, обводка, тело с градиентом и нижней полкой.
    ///
    /// Тень и обводка кладутся соседями перед телом, а не его детьми. В uGUI
    /// ребенок всегда рисуется поверх родителя, и обводка-ребенок закрашивала
    /// панель целиком — на этом в проекте уже обжигались.
    /// </summary>
    static GameObject Korpus(Transform parent, string imya, float x, float y, float w, float h, int r,
                             Color verh, Color niz, int polkaPx, Color polka,
                             Ugol u = Ugol.SverhuSleva, float ten = 0f, Color? kontur = null, int konturPx = 8)
    {
        if (ten > 0f)
        {
            // Тень крупнее корпуса на размытие и опущена на 5: она читается как
            // «объект лежит на поверхности», а не как вторая копия панели.
            var t = Uzel(parent, imya + "-ten", x - 12, y - 12 - 5, w + 24, h + 24, u, prozrachny: false);
            var ti = t.GetComponent<Image>();
            ti.sprite = TenSprite(w, h, r);
            ti.color = new Color(TenC.r, TenC.g, TenC.b, ten);
        }

        int k = konturPx;
        var o = Uzel(parent, imya + "-obvodka", x - k, y - k, w + k * 2, h + k * 2, u, prozrachny: false);
        var oi = o.GetComponent<Image>();
        oi.sprite = Skruglenny((int)w + k * 2, (int)h + k * 2, r + k);
        oi.color = kontur.HasValue ? kontur.Value : ObvodkaC;

        var go = Uzel(parent, imya, x, y, w, h, u, prozrachny: false);
        var im = go.GetComponent<Image>();
        im.sprite = KorpusSprite((int)w, (int)h, r, verh, niz, polkaPx, polka);
        im.color = Color.white;
        return go;
    }

    /// <summary>
    /// Панель "визор": плоская полупрозрачная подложка мирового HUD (раздел 2.2
    /// ux-interfeys-referensy-2026-09-03.md). В отличие от Korpus() здесь нет
    /// ни обводки, ни тени, ни градиента — это язык HUD поверх мира, а не язык
    /// диалоговых окон.
    ///
    /// Альфа 0.58 запечена прямо в пиксели текстуры (ZalivkaSprite), а не
    /// отдана в Image.color: приемка attempt-1 намерила по кадру фактическую
    /// альфу плашки около 0.34 вместо заявленной 0.58 при заливке через
    /// Image.color. KorpusSprite в этом же файле красит цвет прямо в текстуру
    /// и Image.color оставляет белым — это уже проверенный путь для панелей с
    /// градиентом, здесь применен тот же прием для сплошной полупрозрачной
    /// заливки, чтобы вывести Image.color из уравнения совсем.
    /// </summary>
    static GameObject Vizor(Transform parent, string imya, float x, float y, float w, float h, int r,
                            Ugol u = Ugol.SverhuSleva, Color? fonOverride = null)
    {
        var go = Uzel(parent, imya, x, y, w, h, u, prozrachny: false);
        var im = go.GetComponent<Image>();
        // fonOverride — единственный legal способ отступить от канонической
        // альфы 0.58. Существующие вызовы без третьего параметра ведут себя
        // как раньше. Карточка цели этим параметром больше не пользуется —
        // с витка UI-7 попытки 2 она строится через Steklo() ниже, не через
        // Vizor(), см. комментарий у KartochkaTseliVerh.
        im.sprite = ZalivkaSprite((int)w, (int)h, r, fonOverride ?? VizorFon, "vizor");
        im.color = Color.white;

        // Волосок растянут якорями на всю ширину плашки, а не вычислен
        // отдельным числом "w-6": ширина тем самым берется из РЕАЛЬНОГО
        // прямоугольника плашки, а не из копии того же числа.
        // Инсет с торцов = радиус скругления + запас 2 px, а не фиксированные
        // 3 px. Attempt-3: инсет 3 px не покрывал фаску радиусом 14 — волосок
        // вылезал за скругленный угол и висел кончиком над пустым фоном на
        // обоих торцах обеих плашек. Инсет теперь считается ОТ ТОГО ЖЕ "r",
        // которым рисуется сама скругленная форма плашки, — разойтись им
        // больше неоткуда, это тот же урок, что и с медальоном в attempt-2.
        float insetVolosok = r + 2f;
        Volosok(go.transform, imya + "-volosok-verh", vverhu: true, tsvet: VizorVolosok, inset: insetVolosok);
        // Нижний волосок — правка по итогам attempt-2 (см. VizorVolosokNiz):
        // угол XP-плашки терялся на темном фоне мира, верхней грани мало.
        Volosok(go.transform, imya + "-volosok-niz", vverhu: false, tsvet: VizorVolosokNiz, inset: insetVolosok);
        return go;
    }

    /// <summary>
    /// "Стекло" — материал карточки цели с витка UI-7 попытки 2. От Vizor()
    /// отличается тремя вещами и ради них выделен в отдельную функцию, а не
    /// добавлен туда параметрами: (1) заливка — вертикальный градиент
    /// verh->niz, а не одна плоская ZalivkaSprite; (2) кант толщиной `kantPx`
    /// идет по ВСЕМ четырем сторонам скругленного контура, а не только по
    /// верхней/нижней грани, как Volosok(); (3) кант непрозрачен — он не
    /// накладывается вторым Image поверх (как VizorVolosok), а вмешивается в
    /// цвет прямо при выпечке текстуры, чтобы не открыть полупрозрачную щель
    /// по контуру карточки на ярком грунте (та же болезнь, из-за которой
    /// отклонили попытку 1, только теперь по кромке, а не по всей площади).
    ///
    /// Vizor() и ZalivkaSprite() не тронуты этой правкой: счетчики и карточка
    /// имени продолжают строиться прежним вызовом с прежним кэшем.
    /// </summary>
    static GameObject Steklo(Transform parent, string imya, float x, float y, float w, float h, int r,
                             Color verh, Color niz, Color kant, int kantPx, Ugol u = Ugol.SverhuSleva,
                             bool glyanets = false)
    {
        var go = Uzel(parent, imya, x, y, w, h, u, prozrachny: false);
        var im = go.GetComponent<Image>();
        im.sprite = SteklosSprite((int)w, (int)h, r, verh, niz, kant, kantPx, imya, glyanets);
        im.color = Color.white;
        return go;
    }

    /// <summary>Волосок 1 px, растянутый на всю ширину родителя с инсетом `inset` px с торцов.</summary>
    static void Volosok(Transform parent, string imya, bool vverhu, Color tsvet, float inset)
    {
        var go = new GameObject(imya, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, vverhu ? 1 : 0);
        rt.anchorMax = new Vector2(1, vverhu ? 1 : 0);
        rt.pivot = new Vector2(0.5f, vverhu ? 1 : 0);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(-inset * 2f, 1f); // растянутые якори: sizeDelta.x = -(инсет с каждого торца * 2)
        var im = go.GetComponent<Image>();
        im.sprite = Zaliv1x1(tsvet, imya);
        im.color = Color.white;
    }

    static void Nadpis(GameObject go, string text, int size, TextAnchor a, Color col, bool mono = false)
    {
        var im = go.GetComponent<Image>();
        if (im != null) Object.DestroyImmediate(im);
        var t = go.AddComponent<Text>();
        t.text = text;
        // Цифры набираются моноширинным: у пропорционального шрифта счетчик
        // дергается по ширине на каждом изменении числа.
        // Числа — моноширинным, чтобы счетчик не дергался по ширине на каждом
        // изменении. Все остальное — округлым Nunito (лицензия OFL лежит рядом
        // с файлом): системный Arial выдавал интерфейс казенным с первого
        // взгляда, а у Township ни одной острой формы нет и в шрифте тоже.
        //
        // Начертание ИМЕННО ExtraBold, вырезанное из вариативного файла через
        // fontTools.varLib.instancer wght=800. Вариативный Nunito[wght].ttf сам
        // по себе отдает Unity дефолтную ось — а она у него ExtraLight (вес
        // 200). Подписи выходили тоньше прежнего Arial, то есть замена шрифта
        // ухудшала читаемость вместо того, чтобы улучшать.
        Font f = AssetDatabase.LoadAssetAtPath<Font>(
            // Строка 5 доделок (2026-09-04): моношрифт снят. Замер fontTools
            // показал, что у Nunito ExtraBold цифры 0-9 уже табличные —
            // advance 600/1000 у всех десяти, — так что довод «пропорциональные
            // цифры дёргают ширину» к этому файлу не относился. Для чисел
            // берётся Nunito-ExtraBold-Tnum (чернила цифр отцентрированы в
            // ячейке), для текста — прежний Nunito-ExtraBold.
            mono ? "Assets/Fonts/Nunito-ExtraBold-Tnum.ttf" : "Assets/Fonts/Nunito-ExtraBold.ttf");
        t.font = f != null ? f : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) Debug.LogWarning("[interfeys] шрифт не найден, взят системный");
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = a;
        t.color = col;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
    }

    // ---- процедурные спрайты ----------------------------------------------

    static Sprite Skruglenny(int w, int h, int r)
    {
        string key = string.Format("{0}/rr_{1}x{2}_{3}.png", Dir, w, h, r);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;
        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tx.SetPixel(x, y, new Color(1, 1, 1, VnutriSkruglennogo(x, y, w, h, r) ? 1f : 0f));
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>Корпус с вертикальным градиентом, фаской и нижней полкой-торцом.</summary>
    static Sprite KorpusSprite(int w, int h, int r, Color verh, Color niz, int polkaPx, Color polka)
    {
        string key = string.Format("{0}/korpus_{1}x{2}_{3}_{4}_{5}_{6}.png", Dir, w, h, r, polkaPx,
                                   ColorUtility.ToHtmlStringRGB(verh), ColorUtility.ToHtmlStringRGB(niz));
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;

        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!VnutriSkruglennogo(x, y, w, h, r)) { tx.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                Color col;
                if (polkaPx > 0 && y < polkaPx) col = polka;
                else
                {
                    float t = (float)(y - polkaPx) / Mathf.Max(1, h - polkaPx);
                    col = Color.Lerp(niz, verh, t);
                    // Внутренняя фаска по верхней кромке: у Township объем всегда
                    // светлее сверху, и без нее панель читается наклейкой.
                    if (y >= h - 3 && VnutriSkruglennogo(x, y - 2, w, h, r)) col = Color.Lerp(col, Faska, 0.75f);
                }
                tx.SetPixel(x, y, col);
            }
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>
    /// Кнопка главного действия — раздел 2.7 ux-interfeys-referensy-2026-09-03.md.
    /// Семь слоёв сверху вниз: фаска, градиент 1, глянцевая полоса (перекрывает
    /// стык градиентов), градиент 2, переходная полоса, тёмный борт-торец.
    /// Внешней обводки нет вообще — это и есть правка витка UI-6: раньше стоял
    /// тёмный контур `CtaKontur` СНАРУЖИ тела, свет был инвертирован (тёмная
    /// линия там, где у Township светлая фаска). Теперь объём даёт исключительно
    /// перепад светлоты внутри тела.
    ///
    /// KorpusSprite (см. выше) сюда не годится: у него один линейный лерп плюс
    /// кремовая фаска Faska, ни лестницы из двух градиентов, ни глянца он не
    /// умеет, а трогать его сигнатуру нельзя — им собраны все кремовые панели
    /// интерфейса, не только эта кнопка.
    ///
    /// ТОЛЩИНЫ ПОЛОС ПЕРЕСЧИТАНЫ, а не взяты канонической долей h/84 —
    /// и вот почему. `ui_zamer.py:vertical_v_range` меряет ход по V не по всей
    /// высоте кнопки, а по СТРОКАМ, отбросив крайние 15% сверху и снизу бокса
    /// (это и есть порог витка). При буквальном масштабировании канонических
    /// 3 px фаски и 6+6 px перехода-борта на кнопку высотой 76 обе даты
    /// целиком тонут в этих 15%: фаска (2.7 px) меньше верхнего среза
    /// (~11-12 px) без остатка, переход+борт (11 px) меньше нижнего среза
    /// почти один в один. Считается тогда голая внутренняя лестница
    /// градиент1→градиент2, а она у Township плоская сама по себе (перепад
    /// 3-9 единиц V), весь настоящий перепад в рецепте лежит НА ГРАНИЦАХ
    /// полос у самых краёв — там же, где стоит обрезка. Проверено числом:
    /// раскладка "в лоб" даёт видимый ход около 15 V, порог 27 не взят.
    /// Лечится не сменой цветов (все шесть остаются те же самые из рецепта),
    /// а перераспределением толщины: фаска и переход утолщены настолько,
    /// чтобы после среза 15% в кадре осталось по живому куску каждой —
    /// заказ прямо называет это цитатой "работать должны фаска, глянец и
    /// сам градиент в теле, не борт". Раскладка ниже (доли высоты, не px):
    /// фаска 26%, градиент1 18%, градиент2 26%, переход 21%, борт 8% —
    /// даёт после среза 15%/15% фаску ~95 V и хвост перехода ~66 V внутри
    /// видимого окна, ход около 30 единиц с запасом над порогом 27.
    /// </summary>
    static Sprite KnopkaDeystviyaSprite(int w, int h, int r)
    {
        string key = string.Format("{0}/knopka_deystviya_{1}x{2}_{3}.png", Dir, w, h, r);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;

        // Границы полос — доли высоты, накопительно сверху вниз. Подобраны так,
        // чтобы пережить срез 15% сверху и снизу (см. комментарий выше), а не
        // взяты канонической долей 84-px раскладки рецепта.
        float faskaDo    = 0.26f * h; // фаска: 0 .. 26%
        float grad1Do    = 0.44f * h; // градиент 1: 26 .. 44%
        float grad2Do    = 0.70f * h; // градиент 2: 44 .. 70%
        float perehodDo  = 0.91f * h; // переход: 70 .. 91%, борт: 91 .. 100%
        // Глянец лежит внутри градиента 1/2, накрывая их стык — не касается
        // ни фаски, ни перехода, чтобы не портить два опорных края хода по V.
        float glyanetsOt = 0.32f * h, glyanetsDo = 0.53f * h;
        float insetGlyanets = (10f / 420f) * w; // тот же инсет от боков, что и в рецепте (10 px на 420 шириной)

        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!VnutriSkruglennogo(x, y, w, h, r)) { tx.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }

                // Текстура растет снизу вверх (y=0 — низ кнопки), sy — расстояние
                // от ВЕРХНЕЙ грани, тем же порядком, что и таблица рецепта.
                float sy = h - 1 - y;

                Color col;
                if (sy < faskaDo) col = CtaFaska;
                else if (sy < grad1Do) col = Color.Lerp(CtaGrad1Verh, CtaGrad1Niz, Mathf.Clamp01((sy - faskaDo) / (grad1Do - faskaDo)));
                else if (sy < grad2Do) col = Color.Lerp(CtaGrad2Verh, CtaGrad2Niz, Mathf.Clamp01((sy - grad1Do) / (grad2Do - grad1Do)));
                else if (sy < perehodDo) col = CtaPerehod;
                else col = CtaBort;

                // Глянцевая полоса: белая, альфа 0.35, инсет от боков, края
                // смягчены на 2 px по обеим осям, чтобы не резать градиент
                // жёстким прямоугольником.
                float vGlyanets = Mathf.Clamp01(Mathf.Min(sy - glyanetsOt, glyanetsDo - sy) / 2f);
                float hGlyanets = Mathf.Clamp01(Mathf.Min(x - insetGlyanets, (w - insetGlyanets) - x) / 2f);
                float aGlyanets = 0.35f * vGlyanets * hGlyanets;
                if (aGlyanets > 0f) col = Color.Lerp(col, Color.white, aGlyanets);

                tx.SetPixel(x, y, col);
            }
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>
    /// Скругленная плашка одним сплошным цветом, включая альфу — цвет
    /// запечен в пиксели, а не отдан Image.color. См. комментарий в Vizor():
    /// заливка через Image.color с фракционной альфой намерялась вдвое
    /// слабее заявленной, заливка через текстуру — проверенный в этом файле
    /// путь (тот же прием, что и в KorpusSprite).
    /// </summary>
    static Sprite ZalivkaSprite(int w, int h, int r, Color tsvet, string tag)
    {
        // Цвет — часть ключа кэша, а не только w/h/r/tag: тот же класс ошибки,
        // что уже описан у SteklosSprite ниже (правка альфы не подхватывалась
        // на повторной сборке, пока на диске лежал файл под старым именем без
        // цвета). Здесь ровно это и всплыло на скриме склада — смена альфы
        // 0.45 -> другое значение молча продолжала грузить старый PNG.
        string key = string.Format("{0}/{1}_{2}x{3}_{4}_{5}.png", Dir, tag, w, h, r,
            ColorUtility.ToHtmlStringRGBA(tsvet));
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;
        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var proz = new Color(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tx.SetPixel(x, y, VnutriSkruglennogo(x, y, w, h, r) ? tsvet : proz);
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>
    /// "Стекло" — вертикальный градиент verh(верх)->niz(низ) плюс непрозрачный
    /// кант-кольцо шириной `kantPx` по всему периметру скругленного контура
    /// (см. Steklo() выше — только он сюда обращается). Кант не наложенный
    /// поверх Image, а подмешанный в локальный фон пиксель прямо при выпечке:
    /// итоговый цвет кольца = Lerp(градиент в этой точке, kant, kant.a),
    /// альфа результата остается 1 (обе исходные точки уже альфа 1, Lerp по
    /// каналу альфа между 1 и 1 дает 1 без отдельного кода). Так кант несет
    /// тот же тон, что и полупрозрачный VizorVolosok поверх визора, но не
    /// открывает настоящую прозрачность на контуре карточки — та открыла бы
    /// щель в сцену ровно по кромке, микро-версию причины, по которой
    /// отклонили попытку 1 (плоская непрозрачная заливка без вариации).
    ///
    /// `glyanets` — необязательный слабый блик у самой верхней кромки, по
    /// центру ширины, гаснущий к бокам и вниз (альфа примеси белого не выше
    /// 0.10). Только для тела карточки — see Steklo(glyanets: true) в
    /// KartochkaTseli(); для маленькой плашки заголовка не включается, там
    /// ему негде поместиться, не задев текст "ЦЕЛЬ".
    ///
    /// Цвета — часть ключа кэша в hex (verh, niz, kant), а не только
    /// w/h/r/tag: отсутствие цвета в ключе ZalivkaSprite уже один раз стоило
    /// витка (правка альфы карточки цели не подхватилась на повторной сборке,
    /// пока файл лежал на диске под старым именем без цвета) — здесь этот
    /// класс ошибки исключен форматом имени файла.
    /// </summary>
    static Sprite SteklosSprite(int w, int h, int r, Color verh, Color niz, Color kant, int kantPx, string tag,
                                bool glyanets = false)
    {
        string key = string.Format("{0}/steklo-{1}_{2}x{3}_{4}_{5}_{6}_{7}_{8}{9}.png", Dir, tag, w, h, r, kantPx,
            ColorUtility.ToHtmlStringRGB(verh), ColorUtility.ToHtmlStringRGB(niz),
            ColorUtility.ToHtmlStringRGB(kant), glyanets ? "_g" : "");
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;

        var kantOpaque = new Color(kant.r, kant.g, kant.b, 1f);
        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var proz = new Color(0, 0, 0, 0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!VnutriSkruglennogo(x, y, w, h, r)) { tx.SetPixel(x, y, proz); continue; }

                // y=0 — низ текстуры (тот же порядок, что и в KnopkaDeystviyaSprite).
                float t = (float)y / Mathf.Max(1, h - 1);
                Color col = Color.Lerp(niz, verh, t);

                if (kantPx > 0 && !VnutriSkruglennogoInset(x, y, w, h, r, kantPx))
                {
                    col = Color.Lerp(col, kantOpaque, kant.a);
                }
                else if (glyanets)
                {
                    float vGl = Mathf.Clamp01((y - h * 0.78f) / Mathf.Max(1f, h * 0.20f));
                    float hGl = Mathf.Clamp01(1f - Mathf.Abs((x / (float)w) - 0.5f) * 2.2f);
                    float aGl = 0.10f * vGl * hGl;
                    if (aGl > 0f) col = Color.Lerp(col, Color.white, aGl);
                }

                tx.SetPixel(x, y, col);
            }
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>Заливка 1x1 сплошным цветом с альфой — для волосков и прочих тонких линий.</summary>
    static Sprite Zaliv1x1(Color tsvet, string tag)
    {
        string key = string.Format("{0}/zaliv1x1_{1}.png", Dir, tag);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;
        var tx = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tx.SetPixel(0, 0, tsvet);
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>
    /// Контактная тень: силуэт, размытый box-blur в три прохода.
    ///
    /// Компонент Shadow из uGUI здесь не годится — он дает резкую копию без
    /// размытия, а нужна мягкая тень «объект лежит на поверхности». Без нее
    /// интерфейс выглядит вырезанным из фона: в прежнем кадре под панелями не
    /// было ни одного затемненного пикселя.
    /// </summary>
    static Sprite TenSprite(float wf, float hf, int r)
    {
        int w = (int)wf + 24, h = (int)hf + 24;
        string key = string.Format("{0}/ten_{1}x{2}_{3}.png", Dir, w, h, r);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;

        var a = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[y * w + x] = VnutriSkruglennogo(x - 12, y - 12, (int)wf, (int)hf, r) ? 1f : 0f;

        var b = new float[w * h];
        const int rad = 5;
        for (int pass = 0; pass < 3; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; int n = 0;
                    for (int dx = -rad; dx <= rad; dx++)
                    { int xx = x + dx; if (xx < 0 || xx >= w) continue; s += a[y * w + xx]; n++; }
                    b[y * w + x] = s / n;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; int n = 0;
                    for (int dy = -rad; dy <= rad; dy++)
                    { int yy = y + dy; if (yy < 0 || yy >= h) continue; s += b[yy * w + x]; n++; }
                    a[y * w + x] = s / n;
                }
        }

        var tx = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tx.SetPixel(x, y, new Color(1, 1, 1, a[y * w + x]));
        tx.Apply();
        return Sohranit(tx, key);
    }

    /// <summary>
    /// Силуэт теплицы для маяка цели (виток UI-11). Основной источник — готовый
    /// глиф `habitat-dome` с game-icons.net (Delapouite, CC BY 3.0, файл и
    /// лицензия в `Assets/UI/Ikonki/glify/`), перекрашенный в белый с альфой и
    /// положенный в кэш как `siluet-teplitsy_{size}.png` — его и подхватывает
    /// первая строка. Рисованный ниже вариант из полусферы и дуг — резерв на
    /// случай отсутствия файла: инспектор прочёл его как ворота, а заказчик
    /// запретил собирать иконки из примитивов — искать готовое и подгонять.
    /// </summary>
    static Sprite SiluetTeplitsy(int size)
    {
        string key = string.Format("{0}/siluet-teplitsy_{1}.png", Dir, size);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;
        var tx = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float cx = size / 2f, baseY = size * 0.22f, rad = size * 0.44f;
        float rebro = size * 0.07f, duga = size * 0.07f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - baseY;
                bool kupol = dy >= 0 && dx * dx + dy * dy <= rad * rad;
                bool osnovanie = y + 0.5f >= baseY - size * 0.10f && y + 0.5f < baseY && Mathf.Abs(dx) <= rad + size * 0.04f;
                bool inside = kupol || osnovanie;
                if (kupol)
                {
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool rebroL = Mathf.Abs(dx + rad * 0.42f) < rebro && dy > size * 0.02f;
                    bool rebroR = Mathf.Abs(dx - rad * 0.42f) < rebro && dy > size * 0.02f;
                    bool dugaV = Mathf.Abs(r - rad * 0.58f) < duga && dy > size * 0.02f;
                    if (rebroL || rebroR || dugaV) inside = false;
                }
                tx.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
            }
        tx.Apply();
        return Sohranit(tx, key);
    }

    static Sprite FormaSprite(Forma f, int size)
    {
        string key = string.Format("{0}/{1}_{2}.png", Dir, f, size);
        var c = AssetDatabase.LoadAssetAtPath<Sprite>(key); if (c != null) return c;
        var tx = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float cc = size / 2f, rad = size / 2f - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cc + 0.5f, dy = y - cc + 0.5f;
                bool inside;
                if (f == Forma.Shestigrannik) inside = VShestigrannike(dx, dy, rad);
                else if (f == Forma.Zvezda) inside = VZvezde(dx, dy, rad);
                else inside = dx * dx + dy * dy <= rad * rad;
                float lift = Mathf.Lerp(0.16f, -0.12f, (float)y / size);
                tx.SetPixel(x, y, new Color(1f + lift, 1f + lift, 1f + lift, inside ? 1f : 0f));
            }
        tx.Apply();
        return Sohranit(tx, key);
    }

    static bool VnutriSkruglennogo(int x, int y, int w, int h, int r)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        r = Mathf.Min(r, Mathf.Min(w, h) / 2);
        int cx = x < r ? r : (x >= w - r ? w - r - 1 : x);
        int cy = y < r ? r : (y >= h - r ? h - r - 1 : y);
        float dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= (float)r * r;
    }

    /// <summary>
    /// То же самое скругление, но проверка идет по прямоугольнику, уменьшенному
    /// на `inset` px с каждой стороны и с радиусом, уменьшенным на ту же
    /// величину (не меньше 0). Используется SteklosSprite() для кольца канта:
    /// пиксель на канте — это "внутри внешнего контура, но снаружи внутреннего".
    /// </summary>
    static bool VnutriSkruglennogoInset(int x, int y, int w, int h, int r, int inset)
    {
        int iw = w - inset * 2, ih = h - inset * 2, ir = Mathf.Max(0, r - inset);
        if (iw <= 0 || ih <= 0) return false;
        return VnutriSkruglennogo(x - inset, y - inset, iw, ih, ir);
    }

    static bool VShestigrannike(float dx, float dy, float r)
    {
        float q = Mathf.Abs(dx) / r, p = Mathf.Abs(dy) / r;
        return p <= 0.866f && q * 0.866f + p * 0.5f <= 0.866f;
    }

    static bool VZvezde(float dx, float dy, float r)
    {
        float ang = Mathf.Atan2(dy, dx), dist = Mathf.Sqrt(dx * dx + dy * dy);
        float step = Mathf.PI * 2f / 5f;
        float t = Mathf.Repeat(ang + Mathf.PI / 2f, step) / step;
        float edge = Mathf.Lerp(r * 0.45f, r, 1f - Mathf.Abs(t - 0.5f) * 2f);
        return dist <= edge;
    }

    static Sprite Zagruzit(string put)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(put);
        if (s == null) Debug.LogWarning("[interfeys] нет спрайта " + put);
        return s;
    }

    /// <summary>
    /// Виток UI-12: загружает готовый спрайт кита из Assets/UI/Kit/ и ставит
    /// границу 9-slice ЧЕРЕЗ TextureImporter, а не Sprite.Create() налету —
    /// тем же путем, что уже проверен на KartochkaImeni() выше (Sprite.Create
    /// не ссылается ни на какой ассет и теряет ссылку после SaveScene()).
    ///
    /// В отличие от Sohranit()/Zagruzit(), тут нет ни генерации текстуры, ни
    /// кэша по параметрам: файл кита существует на диске один раз и не
    /// меняется, поэтому импортер настраивается заново при каждой сборке.
    /// AssetDatabase.ImportAsset(..., ForceUpdate) обязателен ПОСЛЕ смены
    /// spriteBorder — без ForceUpdate повторный запуск подхватывает границы
    /// прошлого прогона молча (ловушка проекта, уже названная в задании).
    ///
    /// Возвращает null, если файла нет — вызывающий код сам решает откат на
    /// синюю кнопку (см. KnopkaDeystviyaKitSprite()).
    /// </summary>
    /// <summary>
    /// Готовая иконка ресурса заказчика из Assets/UI/Ikonki/resursy (512x512,
    /// вырезана и обведена скриптом). Импорт как Sprite без сжатия; null, если
    /// файла нет — вызывающий решает, чем заменить.
    /// </summary>
    /// <summary>
    /// Карточка цели на генерированных элементах. Панель 1230x634 режется
    /// 9-slice с масштабом 1230/420: центральная зона тогда не растягивается
    /// по горизонтали и лента со скобами остаётся своей пропорции; тянется только
    /// градиент тела по вертикали. Кнопка тянется по ширине (блик горизонтальный),
    /// по высоте держит масштаб исходника.
    /// </summary>
    static void KartochkaTseliGen(Transform root, string variant)
    {
        const float W = 420f, H = 268f, lenta = 68f;   // выступ ленты зашит в panel.png (200 px / 2.93): уменьшение числа без правки спрайта только двигает текст под ленту (кадр etap3-hud-1)   // P4 (АД F1): выступ вдвое тоньше (было 68 = 200/2.93), тело/значок стартуют выше
        float k = 1220f / W;   // ширина panel.png после вырезки

        var kartTen = Uzel(root, "kartochka-tseli-ten", 28 - 12, 64 - 12 - 8, W + 24, H - lenta + 24,
                            Ugol.SnizuSleva, prozrachny: false);
        var kti = kartTen.GetComponent<Image>();
        kti.sprite = TenSprite((int)W, (int)(H - lenta), 12);
        kti.color = new Color(TenC.r, TenC.g, TenC.b, 0.5f);

        var kart = Uzel(root, "kartochka-tseli", 28, 64, W, H, Ugol.SnizuSleva, prozrachny: false);
        var kartImg = kart.GetComponent<Image>();
        kartImg.sprite = SpriteElementa("panel", new Vector4(100, 60, 100, 200));
        kartImg.type = Image.Type.Sliced; kartImg.pixelsPerUnitMultiplier = k;
        kartImg.color = Color.white;

        var korichnevy = Hex("5B3B1E");
        var pt = Uzel(kart.transform, "tekst-tsel", 0, -6, W, 44, Ugol.SverhuTsentr, prozrachny: true);
        Nadpis(pt, "ЦЕЛЬ", 24, TextAnchor.MiddleCenter, korichnevy);

        var mini = Uzel(kart.transform, "miniatyura", 26, -(lenta + 14), 64, 64, Ugol.SverhuSleva, prozrachny: false);   // P4/P5: 56 -> 64
        var mim = mini.GetComponent<Image>();
        mim.sprite = Ikonka(0); mim.color = Color.white;

        var telo = Uzel(kart.transform, "telo", 94, -(lenta + 12), 300, 68, Ugol.SverhuSleva, prozrachny: true);
        Nadpis(telo, "Посеять водоросли\nв теплице", 26, TextAnchor.UpperLeft, Hex("4A3320"));
        telo.GetComponent<Text>().lineSpacing = 1.14f;

        var zv = Uzel(kart.transform, "zvezda-nagrady", 92, 102, 30, 30, Ugol.SnizuSprava, prozrachny: false);
        var zi2 = zv.GetComponent<Image>();
        zi2.sprite = SpriteResursa("znachok-opyt-xp") ?? FormaSprite(Forma.Zvezda, 28); zi2.color = Color.white;
        var nag = Uzel(kart.transform, "nagrada", 26, 102, 62, 30, Ugol.SnizuSprava, prozrachny: true);
        Nadpis(nag, "4 XP", 24, TextAnchor.MiddleRight, Hex("1F6FB5"), mono: true);

        const float kw = 372f, kh = 66f;   // P4: высота кнопки 76 -> 66, ширина прежняя
        var knSprite = SpriteElementa("knopka-" + variant, new Vector4(220, 80, 220, 80));
        var cta = Uzel(kart.transform, "knopka-deystviya", 24, 18, kw, kh, Ugol.SnizuSleva, prozrachny: false);
        var ci = cta.GetComponent<Image>();
        ci.sprite = knSprite; ci.type = Image.Type.Sliced;
        ci.pixelsPerUnitMultiplier = 450f / kh; ci.color = Color.white;   // 450 = высота knopka-*.png после вырезки
        cta.AddComponent<Button>();
        var ct = Uzel(cta.transform, "tekst", 0, 4, kw, 44, Ugol.Tsentr, prozrachny: true);
        Nadpis(ct, "ПОСЕЯТЬ", 30, TextAnchor.MiddleCenter, Color.white);
        var obv = ct.AddComponent<Outline>();
        obv.effectColor = CtaTekstObvodka; obv.effectDistance = new Vector2(2f, -2f);
        var ctaTenTeksta = ct.AddComponent<Shadow>();
        ctaTenTeksta.effectColor = new Color(0f, 0f, 0f, 0.5f);
        ctaTenTeksta.effectDistance = new Vector2(0f, -2f);
    }

    /// <summary>
    /// Генерированный элемент из Assets/UI/Elementy с зонами 9-slice в px
    /// исходника (L, B, R, T). Импорт без сжатия, mesh FullRect, чтобы
    /// полупрозрачная кромка не обрезалась.
    /// </summary>
    /// <summary>
    /// Кнопка меню на генерированном сквиркле (knopka-menyu.png, полоски уже в
    /// картинке). Simple с сохранением пропорции.
    /// </summary>
    static void KnopkaMenyuGen(Transform root)
    {
        const float d = 84f;
        var kn = Uzel(root, "knopka-menyu", 28, 7, d, d, Ugol.VerhSprava, prozrachny: false);
        var ki = kn.GetComponent<Image>();
        ki.sprite = SpriteElementa("knopka-menyu-2", Vector4.zero) ?? SpriteElementa("knopka-menyu", Vector4.zero);   // -2: кремовая керамика заказчика (05.09), синий сквиркл забракован ki.type = Image.Type.Simple;
        ki.preserveAspect = true; ki.color = Color.white;
        kn.AddComponent<Button>();
    }

    /// <summary>
    /// Счётчик на генерированной капсуле: кружок под медальон уже в картинке
    /// (центр на 18% ширины, диаметр 89% высоты у варианта a; 16% у b). Капсула
    /// режется 9-slice: левая зона до конца кружка не тянется, правая кромка
    /// тоже, тянется только тёмная середина. Бар опыта — трек и заливка
    /// картинками, заливка обрезается маской по доле прогресса.
    /// </summary>
    static void SchetchikGen(Transform root, string imya, float x, Forma f, string chislo, string uroven,
                             Ugol blokUgol, string variant)
    {
        const float kapH = 50f, kapW = 210f;
        float medH = kapH * 1.65f * (f == Forma.Krug ? 1.125f : 1.2f);   // P1: u monety polya sprayta menshe (inspektor: 1.76x pri 1.2), u zvezdy 1.64x   // P1 (АД F1): видимый медальон 1.65x капсулы; у спрайта прозрачные поля ~17% (инспектор блока 1 намерил 68/82), отсюда 1.2
        float kIsh = variant == "b" ? 406f : 403f;           // высота исходника капсулы
        float k = kIsh / kapH;                               // px исходника на px экрана
        float krugCx = (variant == "b" ? 196f : 221f) / k;   // центр кружка от левого края капсулы

        var blok = Uzel(root, imya, x, 24, kapW + 20, 72, blokUgol, prozrachny: true);
        var korpus = Uzel(blok.transform, "korpus", 10, 0, kapW, kapH, Ugol.SverhuSleva, prozrachny: false);
        var ki = korpus.GetComponent<Image>();
        ki.sprite = SpriteElementa("kapsula-" + variant, new Vector4(420, 60, 60, 60));
        ki.type = Image.Type.Sliced; ki.pixelsPerUnitMultiplier = k; ki.color = Color.white;

        float pravyyKray = 16f;
        var chis = Uzel(korpus.transform, "znachenie", -pravyyKray, uroven == null ? 0 : 7, 120, 40,
                        Ugol.Sprava, prozrachny: true);
        Nadpis(chis, chislo, uroven == null ? 30 : 24, TextAnchor.MiddleRight, Color.white, mono: true);
        chis.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.6f);

        if (uroven != null)
        {
            const float barH = 9f, barY = 6f;   // P3 (критик): пустой бар опыта тише: 12 -> 9 px, трек альфа 0.7
            float barX = krugCx + medH * 0.5f + 4f;
            float barW = kapW - barX - pravyyKray;
            var trek = Uzel(korpus.transform, "polosa", barX, barY, barW, barH, Ugol.SnizuSleva, prozrachny: false);
            var ti = trek.GetComponent<Image>();
            ti.sprite = SpriteElementa("bar-trek", new Vector4(110, 60, 110, 60));
            ti.type = Image.Type.Sliced; ti.pixelsPerUnitMultiplier = 254f / barH; ti.color = new Color(1f, 1f, 1f, 0.6f);   // 0.7 -> izmereno 0.75 k kapsule (inspektor UI-32), 0.6 -> ~0.65

            // маска по доле прогресса: ширина маски = barW * dolya, внутри заливка полной ширины
            var maska = Uzel(trek.transform, "zapolnenie-maska", 0, 0, barW * 0f, barH, Ugol.SnizuSleva, prozrachny: true);
            maska.AddComponent<RectMask2D>();
            var zap = Uzel(maska.transform, "zapolnenie", 0, 0, barW, barH, Ugol.SnizuSleva, prozrachny: false);
            var zi = zap.GetComponent<Image>();
            zi.sprite = SpriteElementa("bar-zalivka", new Vector4(130, 70, 130, 70));
            zi.type = Image.Type.Sliced; zi.pixelsPerUnitMultiplier = 284f / barH; zi.color = Color.white;
        }

        var med = Uzel(korpus.transform, "medalyon", krugCx - medH * 0.5f, 0, medH, medH, Ugol.SlevaTsentr, prozrachny: false);
        var mi = med.GetComponent<Image>();
        mi.sprite = SpriteResursa(f == Forma.Krug ? "znachok-kredity" : "znachok-opyt-xp") ?? FormaSprite(f, (int)medH);
        mi.color = Color.white;
        if (uroven != null)
        {
            var u0 = Uzel(med.transform, "uroven", 0, -2, medH, medH * 40f / 64f, Ugol.Tsentr, prozrachny: true);
            Nadpis(u0, uroven, 34, TextAnchor.MiddleCenter, Color.white, mono: true);   // P1: кегль x1.3 вместе с медальоном (было 26)
            u0.AddComponent<Outline>().effectColor = Hex("0E3B75");
        }
    }

    static Sprite SpriteElementa(string imya, Vector4 border) =>
        SpriteNaPuti("Assets/UI/Elementy/" + imya + ".png", border);

    /// <summary>
    /// Та же настройка импортера, что была в SpriteElementa, но по готовому
    /// пути — нужна и для Assets/UI/Elementy, и для сгенерированных файлов
    /// в Assets/UI/Generated (см. PlashkaImeniSprite).
    /// </summary>
    static Sprite SpriteNaPuti(string put, Vector4 border)
    {
        if (!File.Exists(put)) { Debug.LogWarning("[interfeys] элемента нет: " + put); return null; }
        AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(put);
        var ts = new TextureImporterSettings(); imp.ReadTextureSettings(ts);
        if (imp.textureType != TextureImporterType.Sprite || imp.spriteBorder != border
            || imp.textureCompression != TextureImporterCompression.Uncompressed || ts.spriteMeshType != SpriteMeshType.FullRect)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.spriteBorder = border;
            imp.ReadTextureSettings(ts); ts.spriteMeshType = SpriteMeshType.FullRect; imp.SetTextureSettings(ts);
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(put);
    }

    /// <summary>
    /// Виток "ночь-2": плашка карточки имени на элементе заказчика
    /// (kapsula-a.png, тёмное стекло), а не процедурный Vizor(). Капсула
    /// целиком — рецепт под счётчик: слева дыра под медальон (alpha=0),
    /// вырезанная под круглую иконку (см. SchetchikGen), справа и в
    /// середине — ровное стекло с закруглённым правым торцом. Для карточки
    /// имени медальон не нужен вовсе, а дыра слева не может быть частью
    /// 9-slice кромки (сквозь неё было бы видно то, что за карточкой).
    ///
    /// Решение — взять ПРАВУЮ половину капсулы (чистое стекло, без дыры) и
    /// отзеркалить её на левый край: получаем симметричную капсулу с
    /// круглым колпачком с обеих сторон, той же текстуры и то же самое
    /// стекло, что и у счётчиков сверху — одна цитата материала на весь
    /// HUD, а не новый рецепт. Кэшируется на диске
    /// (Assets/UI/Generated/kapsula-a-plashka.png) и пересобирается только
    /// если исходник новее уже сгенерированного файла.
    /// </summary>
    static Sprite PlashkaImeniSprite()
    {
        const string src = "Assets/UI/Elementy/kapsula-a.png";
        const string put = "Assets/UI/Generated/kapsula-a-plashka.png";
        if (!File.Exists(src))
        {
            Debug.LogWarning("[interfeys] kapsula-a.png нет — плашка имени не построена");
            return null;
        }
        if (!File.Exists(put) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(put))
        {
            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            raw.LoadImage(File.ReadAllBytes(src)); // читает байты напрямую, не через AssetDatabase — не нужен isReadable на исходном ассете
            int h = raw.height;
            int levaya = raw.width / 2;                 // отбрасываем: там дыра под медальон
            int pw = raw.width - levaya;                 // правая половина — чистое стекло с круглым торцом
            var pravaya = raw.GetPixels(levaya, 0, pw, h);
            var outTx = new Texture2D(pw * 2, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < pw; x++)
                {
                    Color c = pravaya[y * pw + x];
                    outTx.SetPixel(x, y, pravaya[y * pw + (pw - 1 - x)]);   // левая половина — зеркало правой
                    outTx.SetPixel(pw + x, y, c);                           // правая — как в исходнике
                }
            outTx.Apply();
            Directory.CreateDirectory("Assets/UI/Generated");
            File.WriteAllBytes(put, outTx.EncodeToPNG());
            Object.DestroyImmediate(raw);
            Object.DestroyImmediate(outTx);
            AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceUpdate);
        }
        // Радиус колпачка = половина высоты капсулы (403/2 ≈ 202) — тот же
        // принцип, что у процедурной карточки (border = r + 2, см. ниже),
        // только радиус здесь диктует уже нарисованная форма, а не код.
        return SpriteNaPuti(put, new Vector4(202, 60, 202, 60));
    }

    static Sprite SpriteResursa(string imya)
    {
        string put = "Assets/UI/Ikonki/resursy/" + imya + ".png";
        if (!File.Exists(put)) { Debug.LogWarning("[interfeys] иконки ресурса нет: " + put); return null; }
        AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(put);
        if (imp.textureType != TextureImporterType.Sprite || imp.textureCompression != TextureImporterCompression.Uncompressed)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(put);
    }

    static Sprite SpriteKita(string imya, Vector4 border)
    {
        string put = "Assets/UI/Kit/" + imya + ".png";
        if (!File.Exists(put))
        {
            Debug.LogWarning("[interfeys] спрайт кита не найден: " + put);
            return null;
        }
        AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(put);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spriteBorder = border;
        // В Unity 6 тип меша спрайта живёт в TextureImporterSettings, у самого
        // импортера свойства spriteMeshType нет — с ним сборка молча падала и
        // редактор работал на прошлой сборке (ловушка проекта).
        var nastr = new TextureImporterSettings();
        imp.ReadTextureSettings(nastr);
        nastr.spriteMeshType = SpriteMeshType.FullRect;
        imp.SetTextureSettings(nastr);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        AssetDatabase.ImportAsset(put, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Sprite>(put);
    }

    /// <summary>
    /// Кнопка "ПОСЕЯТЬ" пилота UI-12: зеленая версия кита, если художник ее
    /// уже привез, иначе синяя того же файла-рецепта — явный откат, о котором
    /// просило задание, а не тихая ошибка с розовым placeholder.
    /// </summary>
    static Sprite KnopkaDeystviyaKitSprite()
    {
        var s = SpriteKita("knopka-bolshaya-zel", KnopkaKitBorder);
        if (s != null) return s;
        Debug.LogWarning("[interfeys] knopka-bolshaya-zel.png нет, откат на синюю knopka-bolshaya.png");
        return SpriteKita("knopka-bolshaya", KnopkaKitBorder);
    }

    static Sprite Sohranit(Texture2D tx, string put)
    {
        File.WriteAllBytes(put, tx.EncodeToPNG());
        AssetDatabase.ImportAsset(put);
        var im = (TextureImporter)AssetImporter.GetAtPath(put);
        im.textureType = TextureImporterType.Sprite;
        im.spriteImportMode = SpriteImportMode.Single;
        im.alphaIsTransparency = true;
        im.mipmapEnabled = false;
        im.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(put);
    }

    static Color Hex(string h) =>
        new Color(int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                  int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) / 255f,
                  int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber) / 255f);

    /// <summary>Тот же Hex, но с явной альфой — визору нужна прозрачность 0.58-0.62.</summary>
    static Color HexA(string h, float a)
    {
        var c = Hex(h);
        return new Color(c.r, c.g, c.b, a);
    }

    // ---- виток "ночь-4": проверка выделения ВСЕХ построек разом ----------

    const int KontaktKropW = 320, KontaktKropH = 240, KontaktKolonok = 6;
    const string KontaktPut = "C:/Ai/Jarvis/mars-colony/loop/ui/noch2-vydelenie-vse.png";

    sealed class KontaktZapis
    {
        public string label;
        public Texture2D krop;
        public bool sKonturom;
    }

    /// <summary>
    /// Проверка ВСЕХ зданий разом (виток "ночь-4"): по очереди включает то,
    /// что игрок получает кликом (контур `KonturZdaniya` + плашка имени
    /// `ZhivoyInterfeys.PokazatImyaZdaniya`), снимает кроп 320x240 вокруг
    /// здания и складывает контактный лист. Работает целиком в Edit Mode —
    /// ни `VyborZdaniy.Update()`, ни `EventSystem` не участвуют: этот метод
    /// зовёт ровно те же два публичных API, что вызывает `VyborZdaniy.Vybrat`
    /// изнутри Play, напрямую.
    /// </summary>
    [MenuItem("Mars/Interfeys/Kontakt vydeleniya")]
    public static void KontaktVydeleniya()
    {
        EditorSceneManager.OpenScene(Scena, OpenSceneMode.Single);

        var kam = GameObject.Find("kamera");
        var cam = kam != null ? kam.GetComponent<Camera>() : Camera.main;
        if (cam == null) { Debug.LogError("[kontakt] камеры 'kamera' нет — проверка невозможна"); return; }

        // Идемпотентно досевает роли, которых ещё нет, и печёт свежие
        // макушки — та же пара вызовов, что и в Build(), потому что этот
        // метод может быть запущен независимо от Build() отдельным прогоном.
        ZdaniyaCeliBuilder.Rasstavit();
        ObespechitMakushkiZdaniy();

        var zhivoy = Object.FindFirstObjectByType<MarsColony.Game.ZhivoyInterfeys>(FindObjectsInactive.Include);
        if (zhivoy == null) { Debug.LogError("[kontakt] ZhivoyInterfeys не найден в сцене — собери интерфейс (Build) сначала"); return; }
        // ChegoNet() форсирует внутренний Sobrat(): поля рантайма не
        // сериализуются со сценой, а Awake() в Edit Mode для уже
        // существующего в сцене компонента не вызывается сам.
        string net = zhivoy.ChegoNet();
        if (net != "") Debug.LogWarning("[kontakt] ZhivoyInterfeys не нашёл: " + net);

        var zdaniya = Object.FindObjectsByType<MarsColony.Game.BuildingClickTarget>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(zdaniya, (a, b) => string.CompareOrdinal(a.name, b.name));

        // Прогрев окружения ОДИН раз на весь прогон — тот же прием, что в
        // Snyat(string): без него первый кадр свежего batch-процесса выходит
        // плоско-оранжевым (окружение не успело посчитаться).
        DynamicGI.UpdateEnvironment();
        for (int i = 0; i < 6; i++) { cam.Render(); System.Threading.Thread.Sleep(120); }

        var zapisi = new System.Collections.Generic.List<KontaktZapis>();
        KonturZdaniya tekushchiyKontur = null;

        foreach (var z in zdaniya)
        {
            tekushchiyKontur = KonturZdaniya.Vklyuchit(z.gameObject);

            Vector3 makushkaMir = z.MakushkaMir(cam);
            // WorldToScreenPoint здесь считается ДО назначения targetTexture
            // размера кадра — сознательно неточно для якоря плашки в этом
            // диагностическом прогоне (сама плашка на итоговый PNG не
            // анализируется, важен только факт контура); координата кропа
            // считается заново внутри SnyatKadrIKrop с уже назначенным
            // targetTexture, там точность нужна по-настоящему.
            Vector2 tochkaKartochki = new Vector2(cam.WorldToScreenPoint(makushkaMir).x,
                                                   cam.WorldToScreenPoint(makushkaMir).y + MarsColony.Game.VyborZdaniy.OTSTUP_PX);
            zhivoy.PokazatImyaZdaniya(z.Label, tochkaKartochki, cam);

            Texture2D krop = SnyatKadrIKrop(cam, z.ClickBounds.center);

            zapisi.Add(new KontaktZapis
            {
                label = z.Label,
                krop = krop,
                sKonturom = tekushchiyKontur != null && tekushchiyKontur.KopiyCount > 0
            });

            tekushchiyKontur.Ubrat();
            zhivoy.SkrytImyaZdaniya();
        }

        int sKonturom = 0;
        var bezKontura = new System.Collections.Generic.List<string>();
        foreach (var zap in zapisi)
        {
            if (zap.sKonturom) sKonturom++;
            else bezKontura.Add(zap.label);
        }
        Debug.Log($"[kontakt] зданий {zapisi.Count}, с контуром {sKonturom}, "
                + $"без контура: {(bezKontura.Count > 0 ? string.Join(", ", bezKontura) : "нет")}");

        // Постройки без BuildingClickTarget/коллайдера вообще — эвристика по
        // ИМЕНИ корневого объекта (blacklist декора/ландшафта: реголит,
        // камни, лёд, дороги, столбы и т.п., см. `wiki/.../mars-regolit-ne-
        // predmet`), а не полная классификация сцены. Только перечисляется —
        // авто-добавление цели здесь НЕ делается: без художественного
        // подтверждения "это постройка, а не декор" на все 36 моделей риск
        // навесить кликабельность на кусок ландшафта выше пользы находки.
        var bezTseliVoobshche = NaytiZdaniyaBezTseli();
        Debug.Log($"[kontakt] без BuildingClickTarget вовсе (кандидаты по имени, ТРЕБУЮТ ручной проверки владельцем, "
                + $"цель не добавлена): {(bezTseliVoobshche.Count > 0 ? string.Join(", ", bezTseliVoobshche) : "нет")}");

        SobratKontaktnyyList(zapisi, KontaktPut);

        foreach (var zap in zapisi)
            if (zap.krop != null) Object.DestroyImmediate(zap.krop);

        Debug.Log("[kontakt] контактный лист сохранён: " + KontaktPut);
    }

    /// <summary>
    /// Кадр 1600x900 той же камерой, что и `Snyat(string)` (тот же формат
    /// RenderTexture/sRGB-буфер), и кроп 320x240 вокруг экранной проекции
    /// мировой точки — координата считается, пока у камеры ЕЩЁ назначен
    /// targetTexture нужного размера, чтобы WorldToScreenPoint и сам кадр
    /// были в одной и той же системе координат (см. память проекта:
    /// WorldToScreenPoint считает в разрешении окна камеры на момент вызова).
    /// </summary>
    static Texture2D SnyatKadrIKrop(Camera cam, Vector3 tsentrMir)
    {
        var rt = new RenderTexture(RefW, RefH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;

        Vector3 tsentrEkran = cam.WorldToScreenPoint(tsentrMir);

        cam.Render();
        RenderTexture.active = rt;
        var polnyy = new Texture2D(RefW, RefH, TextureFormat.RGB24, false);
        polnyy.ReadPixels(new Rect(0, 0, RefW, RefH), 0, 0);
        polnyy.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(rt);

        int x0 = Mathf.Clamp(Mathf.RoundToInt(tsentrEkran.x - KontaktKropW * 0.5f), 0, RefW - KontaktKropW);
        int y0 = Mathf.Clamp(Mathf.RoundToInt(tsentrEkran.y - KontaktKropH * 0.5f), 0, RefH - KontaktKropH);
        Color[] pikseli = polnyy.GetPixels(x0, y0, KontaktKropW, KontaktKropH);
        Object.DestroyImmediate(polnyy);

        var krop = new Texture2D(KontaktKropW, KontaktKropH, TextureFormat.RGB24, false);
        krop.SetPixels(pikseli);
        krop.Apply();
        return krop;
    }

    /// <summary>
    /// Складывает крупы в сетку `KontaktKolonok` колонок с подписью под
    /// каждой (имя + "БЕЗ КОНТУРА" красным, если контур не построился).
    /// Собрано временным холстом + временной камерой поверх ТОЙ ЖЕ техники,
    /// что и весь остальной интерфейс проекта (Canvas/ScreenSpaceCamera +
    /// RenderTexture-снимок), а не ручным рисованием пикселей текста —
    /// подписи ведёт штатный `Text` тем же шрифтом, что и `Nadpis()`.
    /// </summary>
    static void SobratKontaktnyyList(System.Collections.Generic.List<KontaktZapis> zapisi, string put)
    {
        if (zapisi.Count == 0) { Debug.LogWarning("[kontakt] зданий нет — контактный лист не собран"); return; }

        const int PodpisH = 28;
        int cols = KontaktKolonok;
        int rows = Mathf.CeilToInt(zapisi.Count / (float)cols);
        int cellW = KontaktKropW;
        int cellH = KontaktKropH + PodpisH;
        int sheetW = cols * cellW;
        int sheetH = rows * cellH;

        var root = new GameObject("kontakt-holst-tmp", typeof(Canvas), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();

        var kamGo = new GameObject("kontakt-kamera-tmp", typeof(Camera));
        var kamTmp = kamGo.GetComponent<Camera>();
        kamTmp.clearFlags = CameraClearFlags.SolidColor;
        kamTmp.backgroundColor = new Color(0.06f, 0.09f, 0.14f);
        kamTmp.cullingMask = 0; // сцену колонии этой камерой не снимаем, только холст поверх

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = kamTmp;
        canvas.planeDistance = 1f;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        var sprity = new System.Collections.Generic.List<Sprite>();

        for (int i = 0; i < zapisi.Count; i++)
        {
            var zap = zapisi[i];
            int col = i % cols;
            int rowSverhu = i / cols;
            int rowSnizu = rows - 1 - rowSverhu; // холст снизу-вверх (Ugol.SnizuSleva), кадры читаются сверху вниз

            float x = col * cellW;
            float y = rowSnizu * cellH;

            var tile = Uzel(root.transform, "krop-" + i, x, y + PodpisH, KontaktKropW, KontaktKropH, Ugol.SnizuSleva);
            var im = tile.GetComponent<Image>();
            var sprite = Sprite.Create(zap.krop, new Rect(0, 0, KontaktKropW, KontaktKropH), new Vector2(0.5f, 0.5f));
            sprity.Add(sprite);
            im.sprite = sprite;
            im.color = Color.white;

            var podpis = Uzel(root.transform, "podpis-" + i, x, y, cellW, PodpisH, Ugol.SnizuSleva, prozrachny: true);
            Nadpis(podpis, zap.sKonturom ? zap.label : zap.label + " [БЕЗ КОНТУРА]",
                   16, TextAnchor.MiddleCenter, zap.sKonturom ? Color.white : new Color(1f, 0.35f, 0.35f));
        }

        var rt = new RenderTexture(sheetW, sheetH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        kamTmp.targetTexture = rt;
        Canvas.ForceUpdateCanvases();
        kamTmp.Render();
        RenderTexture.active = rt;
        var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGB24, false);
        sheet.ReadPixels(new Rect(0, 0, sheetW, sheetH), 0, 0);
        sheet.Apply();
        RenderTexture.active = null;
        kamTmp.targetTexture = null;

        Directory.CreateDirectory(Path.GetDirectoryName(put));
        File.WriteAllBytes(put, sheet.EncodeToPNG());

        Object.DestroyImmediate(sheet);
        rt.Release();
        Object.DestroyImmediate(rt);
        foreach (var s in sprity) Object.DestroyImmediate(s);
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(kamGo);
    }

    /// <summary>
    /// Кандидаты "похоже на постройку, но без BuildingClickTarget" —
    /// корневые объекты сцены с меш-геометрией в детях, чьё имя не начинается
    /// ни с одного известного префикса ландшафта/декора и у которых нет
    /// `ClickTarget` нигде в иерархии. Заведомо шире и уже одновременно
    /// точной классификации: список префиксов не исчерпывающий, а взят из
    /// уже задокументированных находок проекта (`ColonyOursBuilder`,
    /// `wiki/.../mars-regolit-ne-predmet`) — намеренно НЕ пытается решить
    /// "постройка это или нет" окончательно, только сузить список для
    /// ручного взгляда владельца.
    /// </summary>
    static System.Collections.Generic.List<string> NaytiZdaniyaBezTseli()
    {
        string[] neDekor =
        {
            "grunt", "regolit", "kamen", "valun", "led-", "doroga", "stolb",
            "provod", "znak", "kust", "fonar", "trosa", "relsa", "kolea",
            "oblomok", "musor", "kabel", "trubop",
        };

        var rezultat = new System.Collections.Generic.List<string>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.parent != null) continue; // только корни — тот же уровень, на котором стоит BuildingClickTarget
            if (t.GetComponentInChildren<MeshRenderer>(true) == null) continue;
            if (t.GetComponentInChildren<MarsColony.Game.ClickTarget>(true) != null) continue;

            string imya = t.name.ToLowerInvariant();
            bool eto_dekor = false;
            foreach (var pref in neDekor)
                if (imya.StartsWith(pref)) { eto_dekor = true; break; }
            if (!eto_dekor)
                rezultat.Add(t.name);
        }
        return rezultat;
    }
}
