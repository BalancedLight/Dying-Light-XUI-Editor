#!/usr/bin/env python3
"""Generate checked-in WPF dictionaries from the canonical English UI catalog.

The translation path deliberately masks format placeholders and Dying Light
technical tokens before sending text to the translation service. Generated
catalogs are validated again before they are written.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import html
import json
import pathlib
import re
import time
import urllib.parse
import urllib.request


ROOT = pathlib.Path(__file__).resolve().parents[1]
LOCALIZATION = ROOT / "src" / "XuiEditor.Wpf" / "Localization"
ENGLISH_PATH = LOCALIZATION / "Strings.En.json"
ALLOWLIST_PATH = LOCALIZATION / "SameAsEnglishAllowlist.json"

LANGUAGES = {
    "En": ("en", "Segoe UI"),
    "De": ("de", "Segoe UI"),
    "Fr": ("fr", "Segoe UI"),
    "It": ("it", "Segoe UI"),
    "Es": ("es", "Segoe UI"),
    "Ru": ("ru", "Segoe UI"),
    "Jp": ("ja", "Yu Gothic UI, Meiryo UI, Segoe UI"),
    "Pl": ("pl", "Segoe UI"),
    "Nl": ("nl", "Segoe UI"),
    "Br": ("pt", "Segoe UI"),
    "Ko": ("ko", "Malgun Gothic, Segoe UI"),
    "Cn": ("zh-CN", "Microsoft YaHei UI, Segoe UI"),
    "Tw": ("zh-TW", "Microsoft JhengHei UI, Segoe UI"),
    "El": ("el", "Segoe UI"),
    "Tr": ("tr", "Segoe UI"),
    "Th": ("th", "Leelawadee UI, Segoe UI"),
    "Cs": ("cs", "Segoe UI"),
}

TRANSLATION_OVERRIDES = {
    ("Fr", "Ui.Animation.Evidence.Stock"): "exact dans le jeu",
    ("Fr", "Ui.Animation.Evidence.Convenience"): "valeur pratique de l'éditeur",
    ("De", "Ui.Command.Reparent"):
        "Übergeordnetes Element von {0} ändern",
    ("De", "Ui.Xaml.GridSettingsWindow.009"): "Haupt",
    ("De", "Ui.Xaml.MainWindow.116"): "Oben",
    ("Fr", "Ui.Command.Reparent"): "Changer le parent de {0}",
    ("It", "Ui.Xaml.MainWindow.022"): "_Riduci rientro",
    ("Es", "Ui.Command.Reparent"): "Cambiar el padre de {0}",
    ("Pl", "Ui.Command.Reparent"): "Zmień element nadrzędny {0}",
    ("Nl", "Ui.Xaml.MainWindow.057"): "XUI openen (Ctrl+O)",
    ("Nl", "Ui.Xaml.MainWindow.070"): "Inzoomen",
    ("Nl", "Ui.Xaml.StockXuiBrowserWindow.002"):
        "Browser voor standaard-XUI's",
    ("Nl", "Ui.Main.Open.Title"): "Dying Light XUI openen",
    ("Br", "Ui.Xaml.MainWindow.022"): "_Diminuir recuo",
    ("El", "Ui.Xaml.MainWindow.035"): "_Προσαρμογή καμβά",
    ("El", "Ui.Xaml.MainWindow.066"): "Προσαρμογή",
    ("El", "Ui.Xaml.MainWindow.043"):
        "Εκκαθάριση αναγκαστικής εμφάνισης",
    ("El", "Ui.Command.Reparent"): "Αλλαγή γονέα του {0}",
    ("Cs", "Ui.Xaml.MainWindow.035"): "_Přizpůsobit plátno",
    ("Cs", "Ui.Xaml.MainWindow.040"): "Vynutit zobrazení výběru",
    ("Cs", "Ui.Xaml.MainWindow.041"):
        "Vynutit zobrazení aktuální skupiny",
    ("Cs", "Ui.Xaml.MainWindow.042"): "Vynutit zobrazení všeho",
    ("Cs", "Ui.Xaml.GridSettingsWindow.003"): "Úroveň",
    ("Cs", "Ui.Xaml.MainWindow.066"): "Přizpůsobit",
    ("Cs", "Ui.Xaml.MainWindow.074"): "Předloha",
    ("Cs", "Ui.Xaml.StockXuiBrowserWindow.001"):
        "Otevřít vestavěné XUI Dying Light",
    ("Cs", "Ui.Command.Reparent"): "Změnit nadřazený prvek {0}",
    ("Cs", "Ui.Diagnostic.Severity.Info"): "Informace",
    ("Cs", "Ui.Main.Status.Reference"): "Předloha: {0}",
    ("Jp", "Ui.Xaml.MainWindow.097"): "インスペクター",
}

STOCK_UI_TRANSLATIONS = {
    "De": {
        "Ui.Xaml.MainWindow.058": "Originale",
        "Ui.Xaml.MainWindow.059":
            "Schreibgeschützte Original-XUIs aus der ausgewählten "
            "Spielinstallation durchsuchen",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Original-XUI von Dying Light öffnen",
        "Ui.Xaml.StockXuiBrowserWindow.002": "Browser für Original-XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Originaldokumente werden schreibgeschützt geöffnet. "
            "Verwende „Speichern unter“, um eine Mod-Kopie zu erstellen.",
    },
    "Fr": {
        "Ui.Xaml.MainWindow.058": "Originaux",
        "Ui.Xaml.MainWindow.059":
            "Parcourir les XUI d’origine en lecture seule de "
            "l’installation de jeu sélectionnée",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Ouvrir un XUI d’origine de Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Navigateur de XUI d’origine",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Les documents d’origine s’ouvrent en lecture seule. "
            "Utilisez Enregistrer sous pour créer une copie de mod.",
    },
    "It": {
        "Ui.Xaml.MainWindow.058": "Originali",
        "Ui.Xaml.MainWindow.059":
            "Sfoglia gli XUI originali di sola lettura "
            "dell’installazione di gioco selezionata",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Apri XUI originale di Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002": "Browser XUI originali",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "I documenti originali vengono aperti in sola lettura. "
            "Usa Salva con nome per creare una copia per la mod.",
    },
    "Es": {
        "Ui.Xaml.MainWindow.058": "Originales",
        "Ui.Xaml.MainWindow.059":
            "Examinar los XUI originales de solo lectura de la "
            "instalación del juego seleccionada",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Abrir XUI original de Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Explorador de XUI originales",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Los documentos originales se abren en modo de solo lectura. "
            "Usa Guardar como para crear una copia para el mod.",
    },
    "Ru": {
        "Ui.Xaml.MainWindow.058": "Оригиналы",
        "Ui.Xaml.MainWindow.059":
            "Просмотреть оригинальные XUI только для чтения из "
            "выбранной установки игры",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Открыть оригинальный XUI Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Обозреватель оригинальных XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Оригинальные документы открываются только для чтения. "
            "Используйте «Сохранить как», чтобы создать копию для мода.",
    },
    "Jp": {
        "Ui.Xaml.MainWindow.058": "オリジナル",
        "Ui.Xaml.MainWindow.059":
            "選択したゲームのインストールから読み取り専用の"
            "オリジナル XUI を参照",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Dying Light のオリジナル XUI を開く",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "オリジナル XUI ブラウザー",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "オリジナル文書は読み取り専用で開きます。"
            "MOD 用のコピーを作成するには"
            "「名前を付けて保存」を使用してください。",
    },
    "Pl": {
        "Ui.Xaml.MainWindow.058": "Oryginalne",
        "Ui.Xaml.MainWindow.059":
            "Przeglądaj oryginalne pliki XUI tylko do odczytu z "
            "wybranej instalacji gry",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Otwórz oryginalny XUI Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Przeglądarka oryginalnych XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Oryginalne dokumenty są otwierane tylko do odczytu. "
            "Użyj opcji Zapisz jako, aby utworzyć kopię dla moda.",
    },
    "Nl": {
        "Ui.Xaml.MainWindow.058": "Origineel",
        "Ui.Xaml.MainWindow.059":
            "Door alleen-lezen originele XUI’s uit de geselecteerde "
            "game-installatie bladeren",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Originele Dying Light-XUI openen",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Browser voor originele XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Originele documenten worden alleen-lezen geopend. "
            "Gebruik Opslaan als om een modkopie te maken.",
    },
    "Br": {
        "Ui.Xaml.MainWindow.058": "Originais",
        "Ui.Xaml.MainWindow.059":
            "Procurar XUIs originais somente leitura na instalação "
            "de jogo selecionada",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Abrir XUI original do Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Navegador de XUI originais",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Os documentos originais são abertos somente para leitura. "
            "Use Salvar como para criar uma cópia para o mod.",
    },
    "Ko": {
        "Ui.Xaml.MainWindow.058": "원본",
        "Ui.Xaml.MainWindow.059":
            "선택한 게임 설치에서 읽기 전용 원본 XUI 찾아보기",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Dying Light 원본 XUI 열기",
        "Ui.Xaml.StockXuiBrowserWindow.002": "원본 XUI 브라우저",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "원본 문서는 읽기 전용으로 열립니다. "
            "모드용 복사본을 만들려면 다른 이름으로 저장을 사용하세요.",
    },
    "Cn": {
        "Ui.Xaml.MainWindow.058": "原版",
        "Ui.Xaml.MainWindow.059": "浏览所选游戏安装中的只读原版 XUI",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "打开 Dying Light 原版 XUI",
        "Ui.Xaml.StockXuiBrowserWindow.002": "原版 XUI 浏览器",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "原版文档以只读方式打开。使用“另存为”创建模组副本。",
    },
    "Tw": {
        "Ui.Xaml.MainWindow.058": "原版",
        "Ui.Xaml.MainWindow.059": "瀏覽所選遊戲安裝中的唯讀原版 XUI",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "開啟 Dying Light 原版 XUI",
        "Ui.Xaml.StockXuiBrowserWindow.002": "原版 XUI 瀏覽器",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "原版文件會以唯讀方式開啟。使用「另存新檔」建立模組副本。",
    },
    "El": {
        "Ui.Xaml.MainWindow.058": "Πρωτότυπα",
        "Ui.Xaml.MainWindow.059":
            "Περιήγηση στα πρωτότυπα XUI μόνο για ανάγνωση από την "
            "επιλεγμένη εγκατάσταση του παιχνιδιού",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Άνοιγμα πρωτότυπου XUI του Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Περιήγηση πρωτότυπων XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Τα πρωτότυπα έγγραφα ανοίγουν μόνο για ανάγνωση. "
            "Χρησιμοποιήστε Αποθήκευση ως για να δημιουργήσετε "
            "αντίγραφο για mod.",
    },
    "Tr": {
        "Ui.Xaml.MainWindow.058": "Orijinaller",
        "Ui.Xaml.MainWindow.059":
            "Seçili oyun kurulumundaki salt okunur orijinal XUI "
            "dosyalarına göz at",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Orijinal Dying Light XUI dosyasını aç",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Orijinal XUI tarayıcısı",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Orijinal belgeler salt okunur açılır. Mod kopyası "
            "oluşturmak için Farklı kaydet seçeneğini kullanın.",
    },
    "Th": {
        "Ui.Xaml.MainWindow.058": "ต้นฉบับ",
        "Ui.Xaml.MainWindow.059":
            "เรียกดู XUI ต้นฉบับแบบอ่านอย่างเดียว"
            "จากการติดตั้งเกมที่เลือก",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "เปิด XUI ต้นฉบับของ Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "เบราว์เซอร์ XUI ต้นฉบับ",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "เอกสารต้นฉบับจะเปิดแบบอ่านอย่างเดียว "
            "ใช้ บันทึกเป็น เพื่อสร้างสำเนาสำหรับม็อด",
    },
    "Cs": {
        "Ui.Xaml.MainWindow.058": "Originály",
        "Ui.Xaml.MainWindow.059":
            "Procházet původní XUI jen pro čtení z vybrané "
            "instalace hry",
        "Ui.Xaml.StockXuiBrowserWindow.001":
            "Otevřít původní XUI Dying Light",
        "Ui.Xaml.StockXuiBrowserWindow.002":
            "Prohlížeč původních XUI",
        "Ui.Xaml.StockXuiBrowserWindow.008":
            "Původní dokumenty se otevírají jen pro čtení. "
            "Pomocí Uložit jako vytvořte kopii pro mod.",
    },
}
for stock_code, stock_entries in STOCK_UI_TRANSLATIONS.items():
    for stock_key, stock_value in stock_entries.items():
        TRANSLATION_OVERRIDES[(stock_code, stock_key)] = stock_value

CORE_UI_TERMS = {
    "De": {
        "Open": "Öffnen",
        "Fit": "Einpassen",
        "Snap": "Einrasten",
        "Reference": "Referenz",
        "Collapse": "Einklappen",
        "Reveal": "Anzeigen",
        "+ Property": "+ Eigenschaft",
        "+ Parent": "+ Übergeordnetes Element",
        "+ Child": "+ Untergeordnetes Element",
        "Inspector": "Eigenschaften",
        "Play": "Wiedergabe",
        "Assets": "Ressourcen",
        "Apply": "Anwenden",
        "Go to": "Gehe zu",
        "Ready": "Bereit",
        "Advanced": "Erweitert",
        "No timeline scope": "Kein Zeitleistenbereich",
        "Include descendants": "Untergeordnete einbeziehen",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Anzeige erzwingen",
        "Composed pose": "Zusammengesetzte Pose",
    },
    "Fr": {
        "Open": "Ouvrir",
        "Fit": "Ajuster",
        "Snap": "Accrochage",
        "Reference": "Référence",
        "Collapse": "Réduire",
        "Reveal": "Afficher",
        "+ Property": "+ Propriété",
        "+ Parent": "+ Élément parent",
        "+ Child": "+ Élément enfant",
        "Inspector": "Inspecteur",
        "Play": "Lire",
        "Assets": "Ressources",
        "Apply": "Appliquer",
        "Go to": "Atteindre",
        "Ready": "Prêt",
        "Advanced": "Avancé",
        "No timeline scope": "Aucune portée de chronologie",
        "Include descendants": "Inclure les descendants",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Forcer l’affichage",
        "Composed pose": "Pose composée",
    },
    "It": {
        "Open": "Apri",
        "Fit": "Adatta",
        "Snap": "Aggancio",
        "Reference": "Riferimento",
        "Collapse": "Comprimi",
        "Reveal": "Mostra",
        "+ Property": "+ Proprietà",
        "+ Parent": "+ Elemento padre",
        "+ Child": "+ Elemento figlio",
        "Inspector": "Ispettore",
        "Play": "Riproduci",
        "Assets": "Risorse",
        "Apply": "Applica",
        "Go to": "Vai a",
        "Ready": "Pronto",
        "Advanced": "Avanzate",
        "No timeline scope": "Nessun ambito della timeline",
        "Include descendants": "Includi discendenti",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Forza visualizzazione",
        "Composed pose": "Posa composta",
    },
    "Es": {
        "Open": "Abrir",
        "Fit": "Ajustar",
        "Snap": "Ajuste",
        "Reference": "Referencia",
        "Collapse": "Contraer",
        "Reveal": "Mostrar",
        "+ Property": "+ Propiedad",
        "+ Parent": "+ Elemento padre",
        "+ Child": "+ Elemento hijo",
        "Inspector": "Inspector",
        "Play": "Reproducir",
        "Assets": "Recursos",
        "Apply": "Aplicar",
        "Go to": "Ir a",
        "Ready": "Listo",
        "Advanced": "Avanzado",
        "No timeline scope": "Sin ámbito de línea de tiempo",
        "Include descendants": "Incluir descendientes",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Forzar visualización",
        "Composed pose": "Pose compuesta",
    },
    "Ru": {
        "Open": "Открыть",
        "Fit": "Вписать",
        "Snap": "Привязка",
        "Reference": "Референс",
        "Collapse": "Свернуть",
        "Reveal": "Показать",
        "+ Property": "+ Свойство",
        "+ Parent": "+ Родительский элемент",
        "+ Child": "+ Дочерний элемент",
        "Inspector": "Инспектор",
        "Play": "Воспроизвести",
        "Assets": "Ресурсы",
        "Apply": "Применить",
        "Go to": "Перейти",
        "Ready": "Готово",
        "Advanced": "Расширенные",
        "No timeline scope": "Область временной шкалы не выбрана",
        "Include descendants": "Включая дочерние элементы",
        "‹ Tick": "‹ Тик",
        "Tick ›": "Тик ›",
        "Tick": "Тик",
        "Force show": "Принудительно показать",
        "Composed pose": "Составная поза",
    },
    "Jp": {
        "Open": "開く",
        "Fit": "全体表示",
        "Snap": "スナップ",
        "Reference": "参照",
        "Collapse": "折りたたむ",
        "Reveal": "表示",
        "+ Property": "+ プロパティ",
        "+ Parent": "+ 親要素",
        "+ Child": "+ 子要素",
        "Inspector": "インスペクター",
        "Play": "再生",
        "Assets": "アセット",
        "Apply": "適用",
        "Go to": "移動",
        "Ready": "準備完了",
        "Advanced": "詳細",
        "No timeline scope": "タイムライン範囲なし",
        "Include descendants": "子孫を含める",
        "‹ Tick": "‹ ティック",
        "Tick ›": "ティック ›",
        "Tick": "ティック",
        "Force show": "強制表示",
        "Composed pose": "合成ポーズ",
    },
    "Pl": {
        "Open": "Otwórz",
        "Fit": "Dopasuj",
        "Snap": "Przyciąganie",
        "Reference": "Odniesienie",
        "Collapse": "Zwiń",
        "Reveal": "Pokaż",
        "+ Property": "+ Właściwość",
        "+ Parent": "+ Element nadrzędny",
        "+ Child": "+ Element podrzędny",
        "Inspector": "Inspektor",
        "Play": "Odtwórz",
        "Assets": "Zasoby",
        "Apply": "Zastosuj",
        "Go to": "Przejdź do",
        "Ready": "Gotowe",
        "Advanced": "Zaawansowane",
        "No timeline scope": "Brak zakresu osi czasu",
        "Include descendants": "Uwzględnij elementy podrzędne",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Wymuś wyświetlenie",
        "Composed pose": "Poza złożona",
    },
    "Nl": {
        "Open": "Openen",
        "Fit": "Inpassen",
        "Snap": "Vastklikken",
        "Reference": "Referentie",
        "Collapse": "Inklappen",
        "Reveal": "Tonen",
        "+ Property": "+ Eigenschap",
        "+ Parent": "+ Bovenliggend element",
        "+ Child": "+ Onderliggend element",
        "Inspector": "Inspector",
        "Play": "Afspelen",
        "Assets": "Middelen",
        "Apply": "Toepassen",
        "Go to": "Ga naar",
        "Ready": "Gereed",
        "Advanced": "Geavanceerd",
        "No timeline scope": "Geen tijdlijnbereik",
        "Include descendants": "Onderliggende elementen opnemen",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Weergave forceren",
        "Composed pose": "Samengestelde pose",
    },
    "Br": {
        "Open": "Abrir",
        "Fit": "Ajustar",
        "Snap": "Encaixe",
        "Reference": "Referência",
        "Collapse": "Recolher",
        "Reveal": "Mostrar",
        "+ Property": "+ Propriedade",
        "+ Parent": "+ Elemento pai",
        "+ Child": "+ Elemento filho",
        "Inspector": "Inspetor",
        "Play": "Reproduzir",
        "Assets": "Recursos",
        "Apply": "Aplicar",
        "Go to": "Ir para",
        "Ready": "Pronto",
        "Advanced": "Avançado",
        "No timeline scope": "Nenhum escopo de linha do tempo",
        "Include descendants": "Incluir descendentes",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Forçar exibição",
        "Composed pose": "Pose composta",
    },
    "Ko": {
        "Open": "열기",
        "Fit": "화면에 맞춤",
        "Snap": "스냅",
        "Reference": "참조",
        "Collapse": "접기",
        "Reveal": "표시",
        "+ Property": "+ 속성",
        "+ Parent": "+ 부모 요소",
        "+ Child": "+ 자식 요소",
        "Inspector": "속성 검사기",
        "Play": "재생",
        "Assets": "에셋",
        "Apply": "적용",
        "Go to": "이동",
        "Ready": "준비",
        "Advanced": "고급",
        "No timeline scope": "타임라인 범위 없음",
        "Include descendants": "하위 요소 포함",
        "‹ Tick": "‹ 틱",
        "Tick ›": "틱 ›",
        "Tick": "틱",
        "Force show": "강제 표시",
        "Composed pose": "합성 포즈",
    },
    "Cn": {
        "Open": "打开",
        "Fit": "适应窗口",
        "Snap": "吸附",
        "Reference": "参考图",
        "Collapse": "折叠",
        "Reveal": "显示",
        "+ Property": "+ 属性",
        "+ Parent": "+ 父元素",
        "+ Child": "+ 子元素",
        "Inspector": "属性检查器",
        "Play": "播放",
        "Assets": "资源",
        "Apply": "应用",
        "Go to": "转到",
        "Ready": "就绪",
        "Advanced": "高级",
        "No timeline scope": "无时间线范围",
        "Include descendants": "包含后代元素",
        "‹ Tick": "‹ 刻",
        "Tick ›": "刻 ›",
        "Tick": "刻",
        "Force show": "强制显示",
        "Composed pose": "合成姿势",
    },
    "Tw": {
        "Open": "開啟",
        "Fit": "符合視窗",
        "Snap": "貼齊",
        "Reference": "參考圖",
        "Collapse": "收合",
        "Reveal": "顯示",
        "+ Property": "+ 屬性",
        "+ Parent": "+ 父元素",
        "+ Child": "+ 子元素",
        "Inspector": "屬性檢查器",
        "Play": "播放",
        "Assets": "資源",
        "Apply": "套用",
        "Go to": "前往",
        "Ready": "就緒",
        "Advanced": "進階",
        "No timeline scope": "無時間軸範圍",
        "Include descendants": "包含後代元素",
        "‹ Tick": "‹ 刻",
        "Tick ›": "刻 ›",
        "Tick": "刻",
        "Force show": "強制顯示",
        "Composed pose": "合成姿勢",
    },
    "El": {
        "Open": "Άνοιγμα",
        "Fit": "Προσαρμογή",
        "Snap": "Προσκόλληση",
        "Reference": "Αναφορά",
        "Collapse": "Σύμπτυξη",
        "Reveal": "Εμφάνιση",
        "+ Property": "+ Ιδιότητα",
        "+ Parent": "+ Γονικό στοιχείο",
        "+ Child": "+ Θυγατρικό στοιχείο",
        "Inspector": "Επιθεωρητής",
        "Play": "Αναπαραγωγή",
        "Assets": "Πόροι",
        "Apply": "Εφαρμογή",
        "Go to": "Μετάβαση σε",
        "Ready": "Έτοιμο",
        "Advanced": "Για προχωρημένους",
        "No timeline scope": "Χωρίς εύρος γραμμής χρόνου",
        "Include descendants": "Συμπερίληψη απογόνων",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Εξαναγκασμένη εμφάνιση",
        "Composed pose": "Σύνθετη πόζα",
    },
    "Tr": {
        "Open": "Aç",
        "Fit": "Sığdır",
        "Snap": "Yakalama",
        "Reference": "Referans",
        "Collapse": "Daralt",
        "Reveal": "Göster",
        "+ Property": "+ Özellik",
        "+ Parent": "+ Üst öğe",
        "+ Child": "+ Alt öğe",
        "Inspector": "Özellik denetçisi",
        "Play": "Oynat",
        "Assets": "Kaynaklar",
        "Apply": "Uygula",
        "Go to": "Git",
        "Ready": "Hazır",
        "Advanced": "Gelişmiş",
        "No timeline scope": "Zaman çizelgesi kapsamı yok",
        "Include descendants": "Alt öğeleri dahil et",
        "‹ Tick": "‹ Tik",
        "Tick ›": "Tik ›",
        "Tick": "Tik",
        "Force show": "Görünümü zorla",
        "Composed pose": "Bileşik poz",
    },
    "Th": {
        "Open": "เปิด",
        "Fit": "พอดีหน้าจอ",
        "Snap": "ยึดตำแหน่ง",
        "Reference": "ภาพอ้างอิง",
        "Collapse": "ยุบ",
        "Reveal": "แสดง",
        "+ Property": "+ คุณสมบัติ",
        "+ Parent": "+ องค์ประกอบแม่",
        "+ Child": "+ องค์ประกอบลูก",
        "Inspector": "ตัวตรวจสอบคุณสมบัติ",
        "Play": "เล่น",
        "Assets": "แอสเซ็ต",
        "Apply": "นำไปใช้",
        "Go to": "ไปที่",
        "Ready": "พร้อม",
        "Advanced": "ขั้นสูง",
        "No timeline scope": "ไม่มีขอบเขตไทม์ไลน์",
        "Include descendants": "รวมองค์ประกอบลูก",
        "‹ Tick": "‹ ติ๊ก",
        "Tick ›": "ติ๊ก ›",
        "Tick": "ติ๊ก",
        "Force show": "บังคับให้แสดง",
        "Composed pose": "ท่าทางแบบผสม",
    },
    "Cs": {
        "Open": "Otevřít",
        "Fit": "Přizpůsobit",
        "Snap": "Přichytávání",
        "Reference": "Předloha",
        "Collapse": "Sbalit",
        "Reveal": "Zobrazit",
        "+ Property": "+ Vlastnost",
        "+ Parent": "+ Nadřazený prvek",
        "+ Child": "+ Podřízený prvek",
        "Inspector": "Inspektor",
        "Play": "Přehrát",
        "Assets": "Prostředky",
        "Apply": "Použít",
        "Go to": "Přejít na",
        "Ready": "Připraveno",
        "Advanced": "Pokročilé",
        "No timeline scope": "Žádný rozsah časové osy",
        "Include descendants": "Zahrnout podřízené prvky",
        "‹ Tick": "‹ Tick",
        "Tick ›": "Tick ›",
        "Tick": "Tick",
        "Force show": "Vynutit zobrazení",
        "Composed pose": "Složená póza",
    },
}

PROPERTY_TOKENS = [
    "ColorControlSequenceEnabled",
    "KeepWidthOnParentSizeChange",
    "KeepHeightOnParentSizeChange",
    "KeepPosXOnParentSizeChange",
    "KeepPosYOnParentSizeChange",
    "KeepWidthOnResolutionChange",
    "KeepHeightOnResolutionChange",
    "KeepPosXOnResolutionChange",
    "KeepPosYOnResolutionChange",
    "HoldAspectPivotPosition",
    "ScaleWidthByResolution",
    "NavTabForward",
    "NavTabBackward",
    "SpecialSignsScale",
    "HorizontalAlign",
    "VerticalAlignDown",
    "VerticalAlign",
    "ClassOverride",
    "HoldAspectRatioX",
    "HoldAspectRatio",
    "OutlineColor",
    "ShadowOffset",
    "ShadowColor",
    "FontYOffset",
    "PointSize",
    "TextColor",
    "TextStyle",
    "ImagePath",
    "OutlineSize",
    "KeepWidth",
    "KeepHeight",
    "KeepPosX",
    "KeepPosY",
    "ClipChildren",
    "NavLeft",
    "NavRight",
    "NavUp",
    "NavDown",
    "Position",
    "Rotation",
    "Material",
    "Opacity",
    "MultiLine",
    "Uppercase",
    "Underline",
    "Outline",
    "Shadow",
    "Visual",
    "Scale",
    "Pivot",
    "Width",
    "Height",
    "Color",
    "Show",
    "Text",
    "Font",
    "Bold",
    "Italic",
    "Strike",
]

TECHNICAL_TOKENS = [
    "ColorControlSequenceEnabled",
    "DyingLightGame.exe",
    "DW\\Data0.pak",
    "menu_antialias.mat",
    "Dying Light",
    "Chrome 6",
    "Microsoft Testing Platform",
    "DualShock 4",
    "Xbox",
    "ClassOverride",
    "XuiVisual",
    "AdvGroup",
    "AdvButton",
    "MyImage",
    "MyText",
    "IUIText",
    "ButtonV",
    "boxed_l_10",
    "Properties",
    "TextStyle",
    "Prop",
    "XUI",
    "XML",
    "PNG",
    "ARGB",
    "WPF",
    "DDS",
    "RP6",
    "RP6L",
    "RPACKs",
    "RPACK",
    "PAKs",
    "PAK",
    "HUD",
    "Id",
]

PLACEHOLDER_RE = re.compile(r"\{[^{}\r\n]+\}|%COLOR\([^)\r\n]+\)")
PATH_RE = re.compile(
    r"(?<![A-Za-z0-9_])(?:"
    r"\*\.(?:xui|png|jpg|jpeg|bmp|def|scr|rpack|mat|exe|pak|ttf|otf)"
    r"|[A-Za-z0-9_/-]+\."
    r"(?:xui|png|jpg|jpeg|bmp|def|scr|rpack|mat|exe|pak|ttf|otf))"
    r"(?![A-Za-z0-9_])",
    re.IGNORECASE,
)
EXACT_TECHNICAL_LABELS = {"X", "Y", "Z", "x"}
MARKER_RE = re.compile(r"<x\d+\s*/>")


def token_pattern(key: str, value: str) -> re.Pattern[str]:
    tokens = list(TECHNICAL_TOKENS)
    normalized = value.replace("_", "").strip()
    is_xaml_property_label = (
        key.startswith("Ui.Xaml.AddXuiElementWindow.")
        and any(
            normalized == token
            or normalized.startswith(f"{token} (")
            for token in PROPERTY_TOKENS
        )
    )
    if (
        key.startswith("Ui.Inspector.Property.")
        or key.startswith("Ui.AddElement.Field.")
        or is_xaml_property_label
    ):
        tokens.extend(PROPERTY_TOKENS)
    return re.compile(
        r"(?<![A-Za-z0-9_])("
        + "|".join(re.escape(token) for token in sorted(
            set(tokens), key=len, reverse=True
        ))
        + r")(?![A-Za-z0-9_])"
    )


def mask_text(value: str, key: str = "") -> tuple[str, list[str]]:
    tokens: list[str] = []

    def mask(match: re.Match[str]) -> str:
        marker = f"<x{len(tokens)}/>"
        tokens.append(match.group(0))
        return marker

    if value in EXACT_TECHNICAL_LABELS:
        return "<x0/>", [value]

    masked = PLACEHOLDER_RE.sub(mask, value)
    masked = PATH_RE.sub(mask, masked)
    masked = token_pattern(key, value).sub(mask, masked)
    masked = re.sub(r"[|]", mask, masked)
    mnemonic_count = masked.count("_")
    masked = masked.replace("_", "")
    for _ in range(mnemonic_count):
        marker = f"<x{len(tokens)}/>"
        tokens.append("_")
        masked = marker + masked
    return masked, tokens


def restore_text(value: str, tokens: list[str]) -> str:
    for index, token in enumerate(tokens):
        value = re.sub(
            rf"<x{index}\s*/>",
            lambda _: token,
            value,
        )
    if MARKER_RE.search(value):
        raise ValueError(f"unrestored protected marker in {value!r}")
    return value


def translate_request(target: str, text: str) -> str:
    query = urllib.parse.urlencode({
        "client": "gtx",
        "sl": "en",
        "tl": target,
        "dt": "t",
        "q": text,
    })
    url = f"https://translate.googleapis.com/translate_a/single?{query}"
    last_error: Exception | None = None
    for attempt in range(5):
        try:
            request = urllib.request.Request(
                url,
                headers={"User-Agent": "DyingLightXuiEditor-localization/1.0"},
            )
            with urllib.request.urlopen(request, timeout=45) as response:
                payload = json.load(response)
            return "".join(part[0] for part in payload[0] if part[0])
        except Exception as exception:  # noqa: BLE001 - retry network failures
            last_error = exception
            time.sleep(1.5 * (attempt + 1))
    raise RuntimeError(f"translation request failed: {last_error}")


def make_batches(
    entries: list[tuple[str, str, list[str]]],
    maximum_chars: int = 3600,
) -> list[list[tuple[str, str, list[str]]]]:
    batches: list[list[tuple[str, str, list[str]]]] = []
    current: list[tuple[str, str, list[str]]] = []
    size = 0
    for entry in entries:
        entry_size = len(entry[1]) + 24
        if current and size + entry_size > maximum_chars:
            batches.append(current)
            current = []
            size = 0
        current.append(entry)
        size += entry_size
    if current:
        batches.append(current)
    return batches


def translate_batch(
    target: str,
    batch: list[tuple[str, str, list[str]]],
) -> list[tuple[str, str]]:
    separators = [
        f"ZXQSEP{index:04d}QXZ" for index in range(len(batch) - 1)
    ]
    pieces: list[str] = []
    for index, (_, masked, _) in enumerate(batch):
        pieces.append(masked)
        if index < len(separators):
            pieces.append(f"\n{separators[index]}\n")
    translated = translate_request(target, "".join(pieces))
    values: list[str] = []
    cursor = 0
    for separator in separators:
        position = translated.find(separator, cursor)
        if position < 0:
            raise ValueError(f"translation removed separator {separator}")
        values.append(translated[cursor:position].strip("\r\n"))
        cursor = position + len(separator)
    values.append(translated[cursor:].strip("\r\n"))
    if len(values) != len(batch):
        raise ValueError("translation batch split count changed")

    result: list[tuple[str, str]] = []
    for (key, _, tokens), translated_value in zip(batch, values, strict=True):
        result.append((key, restore_text(translated_value, tokens)))
    return result


def validate_entry(key: str, source: str, translated: str) -> None:
    if not translated.strip():
        raise ValueError(f"{key}: empty translation")
    source_placeholders = PLACEHOLDER_RE.findall(source)
    translated_placeholders = PLACEHOLDER_RE.findall(translated)
    if sorted(source_placeholders) != sorted(translated_placeholders):
        raise ValueError(
            f"{key}: placeholders changed: "
            f"{source_placeholders!r} != {translated_placeholders!r}"
        )
    for token in PATH_RE.findall(source):
        if token not in translated:
            raise ValueError(f"{key}: protected path changed: {token}")
    for token in token_pattern(key, source).findall(source):
        if token not in translated:
            raise ValueError(f"{key}: protected token changed: {token}")
    for marker in ("|", "\n", "_"):
        if source.count(marker) != translated.count(marker):
            raise ValueError(f"{key}: {marker!r} count changed")


def write_xaml(code: str, catalog: dict[str, str], font: str) -> None:
    lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:system="clr-namespace:System;assembly=System.Runtime"',
        '                    xml:space="preserve">',
        f'    <FontFamily x:Key="Ui.FontFamily">{html.escape(font)}</FontFamily>',
    ]
    for key, value in catalog.items():
        if key == "Ui.FontFamily":
            continue
        escaped_key = html.escape(key, quote=True)
        escaped_value = html.escape(value, quote=False)
        lines.append(
            f'    <system:String x:Key="{escaped_key}">{escaped_value}</system:String>'
        )
    lines.append("</ResourceDictionary>")
    path = LOCALIZATION / f"Strings.{code}.xaml"
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def translate_catalog(
    code: str,
    target: str,
    english: dict[str, str],
) -> dict[str, str]:
    masked_entries: list[tuple[str, str, list[str]]] = []
    for key, value in english.items():
        if key == "Ui.FontFamily":
            continue
        masked, tokens = mask_text(value, key)
        masked_entries.append((key, masked, tokens))

    translated: dict[str, str] = {}
    batches = make_batches(masked_entries)
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        futures = [
            executor.submit(translate_batch, target, batch)
            for batch in batches
        ]
        for future in concurrent.futures.as_completed(futures):
            for key, value in future.result():
                translated[key] = value

    ordered = {
        key: translated[key]
        for key in english
        if key != "Ui.FontFamily"
    }
    for (override_code, key), value in TRANSLATION_OVERRIDES.items():
        if override_code == code:
            ordered[key] = value
    term_overrides = CORE_UI_TERMS.get(code, {})
    for key, source in english.items():
        if key != "Ui.FontFamily" and source in term_overrides:
            ordered[key] = term_overrides[source]
    for key, value in ordered.items():
        validate_entry(key, english[key], value)
    return ordered


def is_narrow_identical(key: str, source: str) -> bool:
    masked, _ = mask_text(source, key)
    remaining_words = re.findall(r"[A-Za-z]{2,}", MARKER_RE.sub("", masked))
    return len(remaining_words) <= 1


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--languages",
        nargs="*",
        choices=list(LANGUAGES),
        default=list(LANGUAGES),
    )
    parser.add_argument(
        "--english-only",
        action="store_true",
        help="only regenerate Strings.En.xaml",
    )
    args = parser.parse_args()

    english = json.loads(ENGLISH_PATH.read_text(encoding="utf-8"))
    if not english or any(not value for value in english.values()):
        raise ValueError("English UI catalog is empty or contains empty values")

    requested = ["En"] if args.english_only else args.languages
    allowlist: dict[str, list[str]] = {}
    for code in requested:
        target, font = LANGUAGES[code]
        if code == "En":
            catalog = dict(english)
        else:
            print(f"Translating {code} ({target})…", flush=True)
            catalog = translate_catalog(code, target, english)
        write_xaml(code, catalog, font)
        identical = [
            key
            for key, source in english.items()
            if key != "Ui.FontFamily" and catalog.get(key) == source
        ]
        suspicious = [] if code == "En" else [
            key for key in identical
            if not is_narrow_identical(key, english[key])
        ]
        if suspicious:
            joined = ", ".join(suspicious[:20])
            raise ValueError(
                f"{code}: untranslated multiword entries: {joined}"
            )
        allowlist[code] = identical
        print(
            f"Wrote Strings.{code}.xaml "
            f"({len(catalog)} keys, {len(identical)} allowlisted identical)",
            flush=True,
        )

    if not args.english_only:
        ALLOWLIST_PATH.write_text(
            json.dumps(allowlist, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )


if __name__ == "__main__":
    main()
