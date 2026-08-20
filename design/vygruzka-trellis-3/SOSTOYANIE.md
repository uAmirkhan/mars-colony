# Состояние партии 3 на паузу

## Сделано 10 из 11 объектов заказа

Все в контракте. Замеры — `zamery.txt`, рендеры по три плюс ноготь —
`rendery/`, контактные листы сверху и снизу.

Оценка глазами и что с чем не так — `zhurnal.md`.

## Осталось

**Из списка А:** `ballony-na-poddone` — упал на нехватке видеопамяти, шёл
первым в очереди. Самый нужный объект заказа, доделать первым.

**Из списка Б, признаны браком и требуют смены подхода, а не параметров:**
`yashchiki-shtabel`, `antenna-tarelka`, `kupol-grib`,
`led-glyba-kristallicheskaya`.

**Список В:** `marsokhod` и `flazhok-na-postamente` — ждут картинок от
владельца, промпты в разделе 7 заказа.

## Как продолжить

```bash
# 1. ОБЯЗАТЕЛЬНО: карта должна быть видна ИЗНУТРИ Ubuntu. После сна или
#    перезагрузки проброс ломается, всё падает на "device not ready".
#    Лечится wsl --shutdown.
wsl -d Ubuntu-24.04 -u root -e nvidia-smi --query-gpu=name --format=csv,noheader

# 2. Сон отключить.
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0

# 3. Продолжить очередь, готовые пропускаются сами.
wsl -d Ubuntu-24.04 -u root -e bash -c "nohup setsid bash /root/partiya3.sh > /root/partiya3.log 2>&1 &"
```

Очередь и параметры — `design/tools/ochered3.txt` и `/root/partiya3.sh`.

## Вопрос, который ждёт ответа

Антенна не даётся четвёртый раз подряд, купол-гриб теряет дверь и окна. Прошу
добро собрать их руками в Blender — как и камни, это тот же класс дефекта:
пересборка поверхности стирает мелкое и вогнутое.
