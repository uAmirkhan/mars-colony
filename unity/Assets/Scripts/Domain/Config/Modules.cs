using System.Collections.Generic;
using System.Linq;

namespace MarsColony.Domain.Config
{
    /// <summary>
    /// Строй-модули и рецепты стройки. Источник истины — [[mars-colony-frame]]
    /// раздел 4 и [[tz-production-mars]] раздел 7; перенесено один в один из
    /// `mars-colony/src/domain/config/modules.ts`.
    ///
    /// Модули — единственная валюта, которую нельзя купить за кредиты (И-1).
    /// </summary>
    public sealed class ModuleDef
    {
        public ModuleId id;
        public string name;
        public ModuleTier tier;
    }

    public sealed class BuildDef
    {
        public string kind;
        public string name;
        public int unlock_level;
        public Dictionary<ModuleId, int> recipe;
        public int build_time_min;
        public bool repeatable;
    }

    public static class Modules
    {
        public static readonly Dictionary<ModuleId, ModuleDef> MODULES = new Dictionary<
            ModuleId,
            ModuleDef
        >
        {
            { ModuleId.panel, new ModuleDef { id = ModuleId.panel, name = "Панель", tier = ModuleTier.basic } },
            { ModuleId.frame, new ModuleDef { id = ModuleId.frame, name = "Каркас", tier = ModuleTier.basic } },
            { ModuleId.sealant, new ModuleDef { id = ModuleId.sealant, name = "Герметик", tier = ModuleTier.rare } },
            { ModuleId.filter, new ModuleDef { id = ModuleId.filter, name = "Фильтр", tier = ModuleTier.rare } },
            { ModuleId.cable, new ModuleDef { id = ModuleId.cable, name = "Кабель", tier = ModuleTier.rare } },
            { ModuleId.drill_head, new ModuleDef { id = ModuleId.drill_head, name = "Буровая головка", tier = ModuleTier.gated } },
            { ModuleId.reactor_cell, new ModuleDef { id = ModuleId.reactor_cell, name = "Реактор-элемент", tier = ModuleTier.gated } },
        };

        public static readonly ModuleId[] ALL_MODULE_IDS = MODULES.Keys.ToArray();

        /// <summary>
        /// Пулы тиров. Вес тира из `TIER_WEIGHTS` делится между модулями пула
        /// поровну: каркас задает вес тира, а не отдельного модуля.
        /// </summary>
        public static readonly Dictionary<ModuleTier, ModuleId[]> MODULE_TIER_POOL = new Dictionary<
            ModuleTier,
            ModuleId[]
        >
        {
            { ModuleTier.basic, ALL_MODULE_IDS.Where(id => MODULES[id].tier == ModuleTier.basic).ToArray() },
            { ModuleTier.rare, ALL_MODULE_IDS.Where(id => MODULES[id].tier == ModuleTier.rare).ToArray() },
            { ModuleTier.gated, ALL_MODULE_IDS.Where(id => MODULES[id].tier == ModuleTier.gated).ToArray() },
        };

        /// <summary>Здания класса Б: строятся из модулей и проходят фазу IN_PROGRESS.</summary>
        public const string BUILD_KIND_WAREHOUSE_UPGRADE = "warehouse_upgrade";
        public const string BUILD_KIND_HABITAT_BLOCK = "habitat_block";

        public static readonly Dictionary<string, BuildDef> CONSTRUCTION_RECIPE = new Dictionary<
            string,
            BuildDef
        >
        {
            {
                BUILD_KIND_WAREHOUSE_UPGRADE,
                new BuildDef
                {
                    kind = BUILD_KIND_WAREHOUSE_UPGRADE,
                    name = "Расширение склада",
                    unlock_level = 5,
                    recipe = new Dictionary<ModuleId, int>
                    {
                        { ModuleId.filter, 6 },
                        { ModuleId.cable, 6 },
                        { ModuleId.sealant, 6 },
                    },
                    build_time_min = 120,
                    repeatable = true,
                }
            },
            {
                BUILD_KIND_HABITAT_BLOCK,
                new BuildDef
                {
                    kind = BUILD_KIND_HABITAT_BLOCK,
                    name = "Жилой блок",
                    unlock_level = 7,
                    recipe = new Dictionary<ModuleId, int>
                    {
                        { ModuleId.panel, 6 },
                        { ModuleId.frame, 5 },
                        { ModuleId.sealant, 7 },
                    },
                    build_time_min = 240,
                    repeatable = false,
                }
            },
        };

        public static readonly string[] ALL_BUILD_KINDS = CONSTRUCTION_RECIPE.Keys.ToArray();

        /// <summary>
        /// Класс Б хранится отдельно от `BuildingType` каркаса намеренно:
        /// `BuildingType` отвечает на вопрос «какое здание производит этот товар»,
        /// а `BuildKind` — на вопрос «что можно построить за модули».
        /// </summary>
        public static readonly Dictionary<string, BuildingType> BUILD_KIND_TO_BUILDING = new Dictionary<
            string,
            BuildingType
        > { { BUILD_KIND_WAREHOUSE_UPGRADE, BuildingType.warehouse } };
    }
}
