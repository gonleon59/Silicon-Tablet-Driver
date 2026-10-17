import Foundation

// Shared by the native UI and the standalone search checks; no AppKit dependency.
enum SettingsPage: String, CaseIterable, Identifiable {
    case tablet, touch, gestures, pen, buttons, diagnostics, general
    var id: String { rawValue }
    var searchTitles: String {
        switch self {
        case .tablet: return "Tablet Displays Планшет экраны мониторы"
        case .touch: return "Touch Касания"
        case .gestures: return "Gestures Жесты"
        case .pen: return "Pen Перо"
        case .buttons: return "Buttons Кнопки"
        case .diagnostics: return "Diagnostics Диагностика"
        case .general: return "General Основные"
        }
    }
}
struct SettingsSearchItem: Identifiable {
    let id: String
    let page: SettingsPage
    let english: String
    let russian: String
    let aliases: String
    func title(russian: Bool) -> String { russian ? self.russian : english }
}
enum SettingsSearchIndex {
    static let items: [SettingsSearchItem] = [
        .init(id: "orientation", page: .tablet, english: "Orientation", russian: "Ориентация", aliases: "right handed left handed vertical portrait landscape rotation правша левша вертикально горизонтально поворот кнопки сверху слева справа 180 270"),
        .init(id: "useMapping", page: .tablet, english: "Use comfortable area presets", russian: "Использовать удобную рабочую область", aliases: "manual mapping tablet pen area ручная область пера позиционирование планшет"),
        .init(id: "areaScale", page: .tablet, english: "Area size", russian: "Размер области", aliases: "working area scale размер рабочей области масштаб площадь пера"),
        .init(id: "edgeMargin", page: .tablet, english: "Edge margin", russian: "Отступ от края", aliases: "working area border край граница рабочая область"),
        .init(id: "nearUser", page: .tablet, english: "Move area closer to me", russian: "Сместить область ближе ко мне", aliases: "near user bottom shift сдвиг к пользователю внизу рабочая область"),
        .init(id: "proportional", page: .tablet, english: "Keep screen proportions", russian: "Сохранять пропорции экрана", aliases: "aspect ratio stretching соотношение сторон растяжение рабочая область"),
        .init(id: "allDisplays", page: .tablet, english: "All connected displays", russian: "Все подключённые мониторы", aliases: "all screens monitors все экраны дисплеи"),
        .init(id: "displays", page: .tablet, english: "Choose displays", russian: "Выбор мониторов", aliases: "selected monitor screens конкретный один несколько экраны дисплеи монитор"),
        .init(id: "touchEnabled", page: .touch, english: "Touch navigation", russian: "Управление касаниями", aliases: "enable disable touch fingers включить отключить тач палец пальцы"),
        .init(id: "PointerSpeed", page: .touch, english: "Pointer speed", russian: "Скорость курсора", aliases: "one finger touch sensitivity чувствительность одного пальца касания"),
        .init(id: "FineMovementSpeed", page: .touch, english: "Fine positioning", russian: "Скорость точного наведения", aliases: "pointer precision slow cursor точность медленно курсор выделение буквы"),
        .init(id: "PointerAcceleration", page: .touch, english: "Pointer acceleration", russian: "Ускорение курсора", aliases: "enable disable включить отключить палец касания"),
        .init(id: "AccelerationStrength", page: .touch, english: "Pointer acceleration strength", russian: "Сила ускорения курсора", aliases: "one finger touch палец касания"),
        .init(id: "PointerSmoothing", page: .touch, english: "Pointer stabilization", russian: "Сглаживание курсора", aliases: "smoothing jitter shaking finger delay latency задержка дрожание дрожит подергивания палец касания"),
        .init(id: "ScrollSpeed", page: .touch, english: "Scrolling speed", russian: "Скорость прокрутки", aliases: "scroll sensitivity two fingers чувствительность два пальца скролл"),
        .init(id: "NaturalScrolling", page: .touch, english: "Natural scrolling", russian: "Естественная прокрутка", aliases: "reverse direction scrolling обратное направление инверсия скролл"),
        .init(id: "ScrollInertia", page: .touch, english: "Scrolling momentum", russian: "Инерция прокрутки", aliases: "inertia scrolling плавная скролл"),
        .init(id: "ScrollAcceleration", page: .touch, english: "Scrolling acceleration", russian: "Ускорение прокрутки", aliases: "enable disable scrolling включить отключить скролл"),
        .init(id: "ScrollAccelerationStrength", page: .touch, english: "Scrolling acceleration strength", russian: "Сила ускорения прокрутки", aliases: "scrolling скролл"),
        .init(id: "resetTouch", page: .touch, english: "Restore recommended touch settings", russian: "Вернуть рекомендуемые настройки касаний", aliases: "reset defaults сброс стандартные по умолчанию"),
        .init(id: "TapToClick", page: .gestures, english: "Tap to click", russian: "Касание для щелчка", aliases: "tap click щелчок клик нажатие"),
        .init(id: "TapAction", page: .gestures, english: "One finger tap", russian: "Касание одним пальцем", aliases: "one finger action left right double click действие щелчок левый правый двойной"),
        .init(id: "TwoFingerTapAction", page: .gestures, english: "Two finger tap", russian: "Касание двумя пальцами", aliases: "two fingers right click context menu два пальца правый щелчок контекстное меню"),
        .init(id: "TapToDrag", page: .gestures, english: "Double tap and hold to drag", russian: "Двойное касание с удержанием для перетаскивания", aliases: "dragging drag drop hold window выделение перенос окна удержать"),
        .init(id: "PageSwipes", page: .gestures, english: "Two finger swipes", russian: "Смахивания двумя пальцами", aliases: "gestures pages back forward жесты переходы страницы назад вперед"),
        .init(id: "SwipeRightAction", page: .gestures, english: "Swipe right", russian: "Смахивание вправо", aliases: "back previous page назад предыдущая страница"),
        .init(id: "SwipeLeftAction", page: .gestures, english: "Swipe left", russian: "Смахивание влево", aliases: "forward next page вперед следующая страница"),
        .init(id: "PinchToZoom", page: .gestures, english: "Pinch to zoom", russian: "Масштаб щипком", aliases: "pinch zoom magnify масштабирование зум увеличение уменьшение щипок"),
        .init(id: "Rotate", page: .gestures, english: "Rotate documents in Preview", russian: "Поворачивать документы в Просмотре", aliases: "rotation document gesture поворот документа жест вращение"),
        .init(id: "hoverSmoothing", page: .pen, english: "Pen stabilization above the tablet", russian: "Сглаживание пера над поверхностью", aliases: "hover smoothing jitter shaking pen delay latency задержка дрожание дрожит перо воздух устойчивость"),
        .init(id: "contactSmoothing", page: .pen, english: "Pen stabilization while touching", russian: "Сглаживание пера при касании", aliases: "contact smoothing jitter shaking pen delay latency задержка дрожание дрожит устойчивость перо поверхность"),
        .init(id: "pressureEnabled", page: .pen, english: "Use pen pressure", russian: "Использовать нажим пера", aliases: "pressure sensitivity drawing pen сила чувствительность нажатия рисование"),
        .init(id: "tipThreshold", page: .pen, english: "Tip activation", russian: "Порог кончика", aliases: "pen pressure threshold click наконечник пера нажим нажатие"),
        .init(id: "eraserThreshold", page: .pen, english: "Eraser activation", russian: "Порог ластика", aliases: "eraser pressure threshold стерка стиратель нажим ластик"),
        .init(id: "penButton0", page: .buttons, english: "Lower pen button", russian: "Нижняя кнопка пера", aliases: "stylus right click боковая правый щелчок"),
        .init(id: "penButton1", page: .buttons, english: "Upper pen button", russian: "Верхняя кнопка пера", aliases: "stylus double click боковая двойной щелчок"),
        .init(id: "bodyButton0", page: .buttons, english: "Tablet button 1", russian: "Кнопка планшета 1", aliases: "expresskeys back назад корпус"),
        .init(id: "bodyButton1", page: .buttons, english: "Tablet button 2", russian: "Кнопка планшета 2", aliases: "expresskeys forward вперед корпус"),
        .init(id: "bodyButton2", page: .buttons, english: "Tablet button 3", russian: "Кнопка планшета 3", aliases: "expresskeys mission control окна корпус"),
        .init(id: "bodyButton3", page: .buttons, english: "Tablet button 4", russian: "Кнопка планшета 4", aliases: "expresskeys desktop рабочий стол корпус"),
        .init(id: "connected", page: .diagnostics, english: "Tablet connection", russian: "Подключение планшета", aliases: "status connected disconnected USB состояние подключен отключен"),
        .init(id: "inputGranted", page: .diagnostics, english: "Input Monitoring", russian: "Мониторинг ввода", aliases: "permissions privacy access разрешения доступ конфиденциальность"),
        .init(id: "accessibilityGranted", page: .diagnostics, english: "Accessibility", russian: "Универсальный доступ", aliases: "permissions privacy access разрешения конфиденциальность"),
        .init(id: "version", page: .diagnostics, english: "Version", russian: "Версия", aliases: "build update сборка обновление"),
        .init(id: "architecture", page: .diagnostics, english: "Architecture", russian: "Архитектура", aliases: "apple silicon arm64 rosetta процессор"),
        .init(id: "diagnostics", page: .diagnostics, english: "Save diagnostics", russian: "Сохранить диагностику", aliases: "export report errors support file экспорт отчет ошибки файл"),
        .init(id: "logs", page: .diagnostics, english: "Open logs folder", russian: "Открыть папку журналов", aliases: "log error crash журнал логи ошибка вылет"),
        .init(id: "test", page: .diagnostics, english: "Test pen and touch", russian: "Проверить перо и касания", aliases: "test pressure buttons eraser диагностика проверка нажим кнопки ластик"),
        .init(id: "language", page: .general, english: "Language", russian: "Язык", aliases: "English Russian русский английский язык интерфейса language translation перевод"),
        .init(id: "theme", page: .general, english: "Theme", russian: "Тема", aliases: "appearance dark light system оформление темная светлая системная"),
        .init(id: "background", page: .general, english: "Background operation", russian: "Фоновая работа", aliases: "menu bar dock close quit tray значок строка меню док закрыть выйти"),
        .init(id: "advanced", page: .general, english: "Open advanced settings", russian: "Открыть расширенные настройки", aliases: "manual geometry plugins shortcuts filters журнал ручная геометрия плагины фильтры сочетания клавиш")
    ]
    static func normalized(_ text: String) -> String {
        text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "en_US_POSIX"))
    }
    static func search(_ query: String, displayNames: [String] = []) -> [SettingsSearchItem] {
        let words = normalized(query).split(whereSeparator: { $0.isWhitespace })
        guard !words.isEmpty else { return [] }
        return items.filter { item in
            let dynamic = item.id == "displays" ? displayNames.joined(separator: " ") : ""
            let isAction = ["TapAction", "TwoFingerTapAction", "SwipeRightAction", "SwipeLeftAction"].contains(item.id)
                || item.id.hasPrefix("penButton") || item.id.hasPrefix("bodyButton")
            let actions = isAction ? "no action left right double click back forward Mission Control show desktop previous next tab без действия левый правый двойной щелчок назад вперед рабочий стол предыдущая следующая вкладка свое назначение custom assignment" : ""
            let text = normalized([item.english, item.russian, item.aliases, item.page.searchTitles, dynamic, actions].joined(separator: " "))
            return words.allSatisfy { text.contains($0) }
        }
    }
}
