using System.Collections.Generic;
using UnityEngine;

namespace MarsColony.Game
{
    /// <summary>
    /// Фоновая музыка колонии (просьба Khan 08.09).
    ///
    /// Треки не зашиты в код: компонент берёт всё, что лежит в `Resources/Audio/muzyka`, и крутит
    /// по кругу в случайном порядке. Положить файл — единственное действие, чтобы музыка появилась;
    /// убрать все файлы — и игра просто идёт молча, без ошибок. Так лицензионный вопрос по треку
    /// решается заменой файла, а не правкой сборки.
    ///
    /// Громкость и выключение живут в `PlayerPrefs`: настройка про устройство игрока, а не про
    /// колонию, и в сейв колонии ей нечего делать.
    /// </summary>
    public sealed class MuzykaFona : MonoBehaviour
    {
        public const string PAPKA = "Audio/muzyka";
        private const string KLYUCH_GROMKOSTI = "muzyka-gromkost";
        /// <summary>Громкость при первом запуске и при включении звука обратно из меню.</summary>
        public const float GROMKOST_PO_UMOLCHANIYU = 0.35f;
        private const float PEREHOD = 1.5f;   // секунды на затухание между треками

        private AudioSource _istochnik;
        private readonly List<AudioClip> _treki = new List<AudioClip>();
        private int _tekushchiy = -1;
        private float _tsel;

        public static MuzykaFona Obespechit(GameObject nositel)
        {
            var m = nositel.GetComponent<MuzykaFona>();
            if (m == null) m = nositel.AddComponent<MuzykaFona>();
            m.Podnyat();
            return m;
        }

        /// <summary>Громкость 0..1; 0 — выключено. Сохраняется между запусками.</summary>
        public static float Gromkost
        {
            get => PlayerPrefs.GetFloat(KLYUCH_GROMKOSTI, GROMKOST_PO_UMOLCHANIYU);
            set
            {
                PlayerPrefs.SetFloat(KLYUCH_GROMKOSTI, Mathf.Clamp01(value));
                PlayerPrefs.Save();
            }
        }

        public bool Igraet => _istochnik != null && _istochnik.isPlaying;
        public string Trek => _tekushchiy >= 0 && _tekushchiy < _treki.Count ? _treki[_tekushchiy].name : null;
        public int Trekov => _treki.Count;

        /// <summary>Идемпотентно: переживает перезагрузку домена в Play, как и остальные Obespechit.</summary>
        private void Podnyat()
        {
            if (_istochnik == null)
            {
                _istochnik = gameObject.GetComponent<AudioSource>();
                if (_istochnik == null) _istochnik = gameObject.AddComponent<AudioSource>();
                _istochnik.playOnAwake = false;
                _istochnik.loop = false;      // цикл делаем сами: между треками нужен переход
                _istochnik.spatialBlend = 0f; // музыка не позиционная
                _istochnik.priority = 0;
            }
            if (_treki.Count == 0)
            {
                var nayden = Resources.LoadAll<AudioClip>(PAPKA);
                if (nayden != null) _treki.AddRange(nayden);
            }
            _tsel = Gromkost;
            if (_treki.Count > 0 && !_istochnik.isPlaying && _tsel > 0f) Sleduyushchiy();
        }

        /// <summary>Подмешать трек, которого нет в Resources (используется проверкой в Play).</summary>
        public void DobavitTrek(AudioClip klip)
        {
            if (klip == null || _treki.Contains(klip)) return;
            _treki.Add(klip);
            if (!_istochnik.isPlaying && Gromkost > 0f) Sleduyushchiy();
        }

        private void Sleduyushchiy()
        {
            if (_treki.Count == 0) return;
            int sled = _treki.Count == 1 ? 0 : (_tekushchiy + 1 + Random.Range(0, _treki.Count - 1)) % _treki.Count;
            _tekushchiy = sled;
            _istochnik.clip = _treki[sled];
            _istochnik.volume = 0f;
            _istochnik.Play();
        }

        private void Update()
        {
            if (_istochnik == null) return;
            _tsel = Gromkost;
            if (_tsel <= 0f)
            {
                if (_istochnik.isPlaying) { _istochnik.volume = 0f; _istochnik.Pause(); }
                return;
            }
            if (!_istochnik.isPlaying)
            {
                if (_istochnik.clip != null && _istochnik.time > 0f) _istochnik.UnPause();
                else Sleduyushchiy();
            }
            // Плавный вход и уход: резкая склейка трека слышна сильнее, чем сама музыка
            float ostalos = _istochnik.clip != null ? _istochnik.clip.length - _istochnik.time : 0f;
            float k = Mathf.Min(_istochnik.time / PEREHOD, ostalos / PEREHOD, 1f);
            _istochnik.volume = _tsel * Mathf.Clamp01(k);
            if (_istochnik.clip != null && ostalos <= 0.05f) Sleduyushchiy();
        }
    }
}
