import AppKit
import SwiftUI
import Combine

// This library is loaded inside the existing GUI process. No helper app, socket or new TCC identity.
private typealias CommandHandler = @convention(c) (UnsafePointer<CChar>?) -> Void
private var commandHandler: CommandHandler?
private var settingsController: SettingsController?
private var activationObserver: NSObjectProtocol?

private struct DisplayInfo: Codable, Equatable, Identifiable {
    var id: String; var name: String; var width: Double; var height: Double; var x: Double; var y: Double
}
private struct Snapshot: Codable, Equatable {
    var profile: String; var version: String; var language: String; var profileToken: String; var settingsToken: String
    var connected: Bool; var hasProfile: Bool; var inputGranted: Bool; var accessibilityGranted: Bool
    var displays: [DisplayInfo]
    var useMapping: Bool; var allDisplays: Bool; var displayIds: [String]; var rotation: Int
    var areaScale: Double; var edgeMargin: Double; var nearUser: Bool; var proportional: Bool
    var touchEnabled: Bool; var touch: [String: Double]; var flags: [String: Bool]; var commands: [String: Int]
    var hoverSmoothing: Double; var contactSmoothing: Double; var tipThreshold: Double; var eraserThreshold: Double
    var pressureEnabled: Bool; var penButtons: [Int]; var bodyButtons: [Int]
}
private func emit(_ value: [String: Any]) {
    guard let callback = commandHandler, let data = try? JSONSerialization.data(withJSONObject: value),
          let text = String(data: data, encoding: .utf8) else { return }
    text.withCString { callback($0) } // The managed callback copies this string synchronously.
}
private final class SettingsModel: ObservableObject {
    @Published var draft: Snapshot
    @Published var page: SettingsPage? = .tablet
    @Published var search = ""
    @Published var revealedSetting: String?
    var searching: Bool { !search.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    var searchResults: [SettingsSearchItem] { SettingsSearchIndex.search(search, displayNames: draft.displays.map(\.name)) }
    func reveal(_ item: SettingsSearchItem) {
        page = item.page; search = ""; revealedSetting = item.id
    }
    func setLanguage(_ code: String) {
        guard !busy, ["en", "ru"].contains(code), code != draft.language else { return }
        busy = true; message = ""
        emit(["action": "language", "language": code])
    }
    @Published var busy = false
    @Published var message = ""
    @Published var error: String?
    @Published var numericDrafts: [String: NumericDraft] = [:]
    var hasInvalidNumbers: Bool { numericDrafts.values.contains { !$0.valid } }
    func editNumber(_ id: String, text: String, value: Binding<Double>, range: ClosedRange<Double>, multiplier: Double) {
        // SwiftUI can echo the displayed text while creating/focusing a field.
        // Reading a rounded float representation must not dirty or rewrite the profile.
        let normalized = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        if numericDrafts[id] == nil && normalized == BambooNumericInput.format(value.wrappedValue, multiplier: multiplier, russian: false) { return }
        let edit = NumericDraft(text: text, range: range, multiplier: multiplier)
        numericDrafts[id] = edit
        if case let .valid(number) = edit.result { value.wrappedValue = number }
    }
    func finishNumber(_ id: String, value: Binding<Double>) {
        guard let edit = numericDrafts[id] else { return }
        if case let .valid(number) = BambooNumericInput.parse(edit.text, range: edit.range, multiplier: edit.multiplier, finalizing: true) {
            value.wrappedValue = number
            numericDrafts.removeValue(forKey: id)
        }
    }
    func revealNumericError() {
        if let item = SettingsSearchIndex.items.first(where: { numericDrafts[$0.id]?.valid == false }) { reveal(item) }
    }
    private var original: Snapshot
    var dirty: Bool { draft != original || hasInvalidNumbers }
    var russian: Bool { draft.language == "ru" }
    func t(_ en: String, _ ru: String) -> String { russian ? ru : en }
    init(_ value: Snapshot) { draft = value; original = value }
    func update(_ value: Snapshot) { numericDrafts.removeAll(); draft = value; original = value; message = "" }
    func discard() { numericDrafts.removeAll(); draft = original; message = "" }
    func save() {
        guard !hasInvalidNumbers else {
            error = t("Check the highlighted numeric fields before saving.", "Проверьте выделенные числовые поля перед сохранением.")
            return
        }
        guard !busy, let data = try? JSONEncoder().encode(draft),
              let settings = try? JSONSerialization.jsonObject(with: data) else { return }
        busy = true; message = ""
        emit(["action": "save", "settings": settings])
    }
    func action(_ name: String) {
        guard !busy else { return }
        if ["refresh", "advanced", "quit"].contains(name), dirty {
            let alert = NSAlert()
            alert.messageText = t("Unsaved changes", "Есть несохранённые изменения")
            alert.informativeText = t("Keep editing or discard these changes before continuing.", "Продолжите настройку или отмените изменения перед переходом.")
            alert.addButton(withTitle: t("Keep editing", "Продолжить настройку"))
            alert.addButton(withTitle: t("Discard changes", "Отменить изменения"))
            guard alert.runModal() == .alertSecondButtonReturn else { return }
            discard()
        }
        busy = true; message = ""; emit(["action": name])
    }
    func diagnostics() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "OpenTabletDriver-diagnostics.json"
        panel.canCreateDirectories = true
        panel.title = t("Save diagnostics", "Сохранить диагностику")
        panel.begin { [weak self] response in
            guard response == .OK, let url = panel.url, let self else { return }
            self.busy = true; emit(["action": "diagnostics", "path": url.path])
        }
    }
    func receive(_ json: String) {
        guard let data = json.data(using: .utf8), let value = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        if let status = value["status"] as? [String: Bool] {
            if let v=status["connected"] { draft.connected=v; original.connected=v }
            if let v=status["inputGranted"] { draft.inputGranted=v; original.inputGranted=v }
            if let v=status["accessibilityGranted"] { draft.accessibilityGranted=v; original.accessibilityGranted=v }
            return
        }
        busy = false
        if (value["success"] as? Bool) == true {
            if let language = value["language"] as? String, ["en", "ru"].contains(language) {
                // Language is an independent preference; retain unsaved tablet edits.
                draft.language = language; original.language = language; message = ""
                installEditingMenu(russian: language == "ru")
                return
            }
            if let raw = value["settings"] as? [String: Any], let encoded = try? JSONSerialization.data(withJSONObject: raw),
               let snapshot = try? JSONDecoder().decode(Snapshot.self, from: encoded) {
                update(snapshot)
                message = (value["saved"] as? Bool) == true ? t("Settings saved", "Настройки сохранены") : ""
            } else { message = t("Done", "Готово") }
        } else { error = value["error"] as? String ?? t("Could not complete the operation.", "Не удалось выполнить действие.") }
    }
    func number(_ key: String) -> Binding<Double> {
        Binding(get: { self.draft.touch[key] ?? 0 }, set: { self.draft.touch[key] = $0; self.message = "" })
    }
    func flag(_ key: String) -> Binding<Bool> {
        Binding(get: { self.draft.flags[key] ?? false }, set: { self.draft.flags[key] = $0; self.message = "" })
    }
    func command(_ key: String) -> Binding<Int> {
        Binding(get: { self.draft.commands[key] ?? 0 }, set: { self.draft.commands[key] = $0; self.message = "" })
    }
    func button(_ index: Int, pen: Bool) -> Binding<Int> {
        Binding(get: { pen ? self.draft.penButtons[index] : self.draft.bodyButtons[index] }, set: {
            if pen { self.draft.penButtons[index] = $0 } else { self.draft.bodyButtons[index] = $0 }
        })
    }
    func resetTouch() {
        for key in draft.touch.keys { numericDrafts.removeValue(forKey: key) }
        draft.touch = ["PointerSpeed": 1.5, "FineMovementSpeed": 0.65, "AccelerationStrength": 0.4, "PointerSmoothing": 40, "ScrollSpeed": 0.25, "ScrollAccelerationStrength": 0.4]
        for key in draft.flags.keys { draft.flags[key] = true }
        draft.commands = ["TapAction": 1, "TwoFingerTapAction": 2, "SwipeRightAction": 4, "SwipeLeftAction": 5]
        draft.touchEnabled = true
    }
}
private extension SettingsPage {
    var symbol: String {
        switch self {
        case .tablet: return "display.2"
        case .touch: return "cursorarrow.motionlines"
        case .gestures: return "hand.draw"
        case .pen: return "pencil.tip"
        case .buttons: return "rectangle.and.hand.point.up.left"
        case .diagnostics: return "waveform.path.ecg"
        case .general: return "gearshape"
        }
    }
    var color: Color {
        switch self { case .tablet: return .blue; case .touch: return .indigo; case .gestures: return .purple; case .pen: return .orange; case .buttons: return .teal; case .diagnostics: return .green; case .general: return .gray }
    }
    func title(_ m: SettingsModel) -> String {
        switch self {
        case .tablet: return m.t("Tablet & Displays", "Планшет и экраны")
        case .touch: return m.t("Touch", "Касания")
        case .gestures: return m.t("Gestures", "Жесты")
        case .pen: return m.t("Pen", "Перо")
        case .buttons: return m.t("Buttons", "Кнопки")
        case .diagnostics: return m.t("Diagnostics", "Диагностика")
        case .general: return m.t("General", "Основные")
        }
    }
    func subtitle(_ m: SettingsModel) -> String {
        switch self {
        case .tablet: return m.t("Orientation, working area and the displays you use.", "Ориентация, удобная рабочая область и нужные мониторы.")
        case .touch: return m.t("A steady pointer and comfortable scrolling.", "Точное наведение курсора и удобная прокрутка.")
        case .gestures: return m.t("Choose what your finger gestures do.", "Выберите действия для касаний и движений пальцами.")
        case .pen: return m.t("Stability, pressure and the eraser.", "Устойчивость, нажим и ластик.")
        case .buttons: return m.t("Shortcuts at your fingertips.", "Частые действия под рукой.")
        case .diagnostics: return m.t("Connection, permissions and a report for troubleshooting.", "Подключение, разрешения и отчёт для поиска неполадок.")
        case .general: return m.t("Language, background operation and advanced settings.", "Язык, фоновая работа и расширенные настройки.")
        }
    }
}
private struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    private var current: SettingsPage { model.page ?? .tablet }
    var body: some View {
        NavigationSplitView {
            VStack(spacing: 0) {
                HStack(spacing: 12) {
                    Image(systemName: "rectangle.landscape.and.arrow.up.right").font(.title2).foregroundStyle(.tint)
                    VStack(alignment: .leading, spacing: 3) {
                        Text("Bamboo").font(.headline)
                        Text("Wacom CTH-661").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                }.padding(.horizontal, 18).padding(.vertical, 20)
                HStack(spacing: 4) {
                    TextField(model.t("Search settings", "Поиск настроек"), text: $model.search)
                        .textFieldStyle(.roundedBorder)
                    if !model.search.isEmpty {
                        Button { model.search = "" } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                            .buttonStyle(.plain).accessibilityLabel(model.t("Clear search", "Очистить поиск"))
                    }
                }.padding(.horizontal, 12).padding(.bottom, 10)
                List(selection: $model.page) {
                    ForEach(SettingsPage.allCases) { item in
                        Label {
                            Text(item.title(model))
                        } icon: {
                            Image(systemName: item.symbol).font(.system(size: 13, weight: .semibold))
                                .foregroundStyle(.white).frame(width: 25, height: 25)
                                .background(item.color.gradient, in: RoundedRectangle(cornerRadius: 6))
                        }.padding(.vertical, 4).tag(item)
                    }
                }.listStyle(.sidebar)
                HStack(spacing: 6) {
                    Circle().fill(model.draft.connected ? .green : .orange).frame(width: 7, height: 7)
                    Text(model.draft.connected ? model.t("Tablet connected", "Планшет подключён") : model.t("Tablet disconnected", "Планшет отключён"))
                        .font(.caption).foregroundStyle(.secondary)
                    Spacer()
                }.padding(16)
            }.navigationSplitViewColumnWidth(min: 210, ideal: 230, max: 260)
        } detail: {
            VStack(spacing: 0) {
                ScrollViewReader { proxy in
                    Form {
                        if model.searching { searchResults }
                        else {
                            header
                            Group {
                                switch current {
                                case .tablet: tabletPage
                                case .touch: touchPage
                                case .gestures: gesturesPage
                                case .pen: penPage
                                case .buttons: buttonsPage
                                case .diagnostics: diagnosticsPage
                                case .general: generalPage
                                }
                            }.disabled(model.busy || (!model.draft.hasProfile && current != .diagnostics && current != .general))
                        }
                    }.formStyle(.grouped)
                    .onChange(of: model.revealedSetting) { _, target in
                        guard let target else { return }
                        // The selected page must lay out its rows before scrolling.
                        DispatchQueue.main.async { withAnimation { proxy.scrollTo(target, anchor: .center) } }
                    }
                }
                Divider()
                HStack(spacing: 12) {
                    if model.busy { ProgressView().controlSize(.small) }
                    Text(model.busy ? model.t("Applying…", "Применение…") : model.hasInvalidNumbers ? model.t("Check numeric fields", "Проверьте числовые поля") : model.dirty ? model.t("Unsaved changes", "Есть изменения") : model.message)
                        .font(.callout).foregroundStyle(.secondary).lineLimit(1)
                    if model.hasInvalidNumbers {
                        Button(model.t("Show", "Показать")) { model.revealNumericError() }
                    }
                    Spacer()
                    Button(model.t("Revert", "Отменить")) { model.discard() }.disabled(!model.dirty || model.busy)
                    Button(model.t("Save", "Сохранить")) { model.save() }
                        .buttonStyle(.borderedProminent).keyboardShortcut("s", modifiers: .command)
                        .disabled(!model.dirty || model.busy || model.hasInvalidNumbers || !model.draft.hasProfile)
                }.padding(.horizontal, 22).padding(.vertical, 14).background(.bar)
            }.navigationTitle(model.searching ? model.t("Search settings", "Поиск настроек") : current.title(model))
                .toolbar { ToolbarItem { Button { model.action("refresh") } label: { Image(systemName: "arrow.clockwise") }.help(model.t("Refresh connection and displays", "Обновить подключение и мониторы")).disabled(model.busy) } }
        }
        .frame(minWidth: 850, minHeight: 600)
        .onReceive(Timer.publish(every: 2, on: .main, in: .common).autoconnect()) { _ in
            if settingsController?.window?.isVisible == true && !model.busy { emit(["action": "status"]) }
        }
        .onChange(of: model.page) {
            if model.searching { model.search = ""; model.revealedSetting = nil }
        }
        .onChange(of: model.search) { _, query in
            if !query.isEmpty { model.revealedSetting = nil }
        }
        .environment(\.locale, Locale(identifier: model.russian ? "ru_RU" : "en_US"))
        .alert(model.t("Could not save", "Не удалось выполнить действие"), isPresented: Binding(get: { model.error != nil }, set: { if !$0 { model.error = nil } })) {
            Button("OK") { model.error = nil }
        } message: { Text(model.error ?? "") }
    }
    private var searchResults: some View {
        Group {
            Section {
                Text(model.t("Search results", "Результаты поиска")).font(.title2.bold()).padding(.top, 12)
                Text(model.t("Search in English or Russian, regardless of the interface language.", "Ищет по английским и русским названиям независимо от языка интерфейса."))
                    .font(.callout).foregroundStyle(.secondary)
            }
            if model.searchResults.isEmpty {
                Section {
                    Label(model.t("No settings found", "Настройки не найдены"), systemImage: "magnifyingglass")
                    Text(model.t("Try a shorter query or another name.", "Попробуйте более короткий запрос или другое название."))
                        .foregroundStyle(.secondary)
                }
            } else {
                Section(model.t("Found", "Найдено") + ": \(model.searchResults.count)") {
                    ForEach(model.searchResults) { item in
                        Button { model.reveal(item) } label: {
                            HStack(spacing: 12) {
                                Image(systemName: item.page.symbol).foregroundStyle(item.page.color).frame(width: 24)
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(item.title(russian: model.russian)).foregroundStyle(.primary)
                                    Text(item.page.title(model)).font(.caption).foregroundStyle(.secondary)
                                }
                                Spacer()
                                Image(systemName: "chevron.right").font(.caption).foregroundStyle(.tertiary)
                            }.padding(.vertical, 4).contentShape(Rectangle())
                        }.buttonStyle(.plain)
                    }
                }
            }
        }
    }
    private func setting(_ id: String) -> some ViewModifier { SettingAnchor(id: id, selected: model.revealedSetting == id) }
    private var header: some View {
        VStack(spacing: 9) {
            Image(systemName: current.symbol).font(.system(size: 28, weight: .medium)).foregroundStyle(.white)
                .frame(width: 62, height: 62).background(current.color.gradient, in: RoundedRectangle(cornerRadius: 15))
            Text(current.title(model)).font(.title2.bold())
            Text(current.subtitle(model)).font(.callout).foregroundStyle(.secondary).multilineTextAlignment(.center)
            if !model.draft.hasProfile { Text(model.t("Connect your Bamboo tablet to configure it.", "Подключите Bamboo, чтобы настроить планшет.")).foregroundStyle(.orange) }
        }.frame(maxWidth: .infinity).padding(.vertical, 12).listRowBackground(Color.clear)
    }
    private var tabletPage: some View {
        Group {
            Section {
                TabletPreview(model: model).frame(height: 170).frame(maxWidth: .infinity)
                Picker(model.t("Orientation", "Ориентация"), selection: $model.draft.rotation) {
                    Text(model.t("Right handed · buttons left", "Правша · кнопки слева")).tag(0)
                    Text(model.t("Left handed · buttons right", "Левша · кнопки справа")).tag(180)
                    Text(model.t("Vertical · buttons top", "Вертикально · кнопки сверху")).tag(270)
                }.disabled(!model.draft.useMapping).modifier(setting("orientation"))
                if model.draft.rotation == 270 { Text(model.t("The four tablet buttons are disabled in this orientation.", "В этой ориентации четыре кнопки корпуса отключены.")).font(.caption).foregroundStyle(.secondary) }
            }
            Section(model.t("Working area", "Рабочая область")) {
                Toggle(model.t("Use comfortable area presets", "Использовать удобную рабочую область"), isOn: $model.draft.useMapping).modifier(setting("useMapping"))
                Group {
                    slider("areaScale", model.t("Area size", "Размер области"), value: $model.draft.areaScale, range: 0.3...1, unit: "%", multiplier: 100).modifier(setting("areaScale"))
                    slider("edgeMargin", model.t("Edge margin", "Отступ от края"), value: $model.draft.edgeMargin, range: 0...30, unit: model.t("mm", "мм")).modifier(setting("edgeMargin"))
                    Toggle(model.t("Move area closer to me", "Сместить область ближе ко мне"), isOn: $model.draft.nearUser).modifier(setting("nearUser"))
                    Toggle(model.t("Keep screen proportions", "Сохранять пропорции экрана"), isOn: $model.draft.proportional).modifier(setting("proportional"))
                }.disabled(!model.draft.useMapping)
                if !model.draft.useMapping { Text(model.t("Your manual area is preserved. Use Advanced Settings to edit its exact dimensions.", "Ручная область сохраняется. Точные размеры доступны в расширенных настройках.")).font(.caption).foregroundStyle(.secondary) }
            }
            Section(model.t("Displays", "Мониторы")) {
                Toggle(model.t("All connected displays", "Все подключённые мониторы"), isOn: $model.draft.allDisplays).modifier(setting("allDisplays"))
                ForEach(model.draft.displays) { display in
                    Toggle(isOn: Binding(get: { model.draft.allDisplays || model.draft.displayIds.contains(display.id) }, set: { enabled in
                        if model.draft.allDisplays { model.draft.displayIds = model.draft.displays.map(\.id); model.draft.allDisplays = false }
                        if enabled { if !model.draft.displayIds.contains(display.id) { model.draft.displayIds.append(display.id) } }
                        else { model.draft.displayIds.removeAll { $0 == display.id } }
                    })) {
                        Label { VStack(alignment: .leading, spacing: 2) { Text(display.name); Text("\(Int(display.width)) × \(Int(display.height))").font(.caption).foregroundStyle(.secondary) } } icon: { Image(systemName: "display").foregroundStyle(.secondary) }
                    }
                }
            }.disabled(!model.draft.useMapping).modifier(setting("displays"))
        }
    }
    private var touchPage: some View {
        Group {
            Section { Toggle(model.t("Touch navigation", "Управление касаниями"), isOn: $model.draft.touchEnabled).modifier(setting("touchEnabled")) }
            Section(model.t("Pointer", "Курсор")) {
                slider("PointerSpeed", model.t("Speed", "Скорость"), value: model.number("PointerSpeed"), range: 0.1...4, unit: "×").modifier(setting("PointerSpeed"))
                slider("FineMovementSpeed", model.t("Fine positioning", "Скорость точного наведения"), value: model.number("FineMovementSpeed"), range: 0.2...1, unit: "%", multiplier: 100).modifier(setting("FineMovementSpeed"))
                Toggle(model.t("Acceleration", "Ускорение"), isOn: model.flag("PointerAcceleration")).modifier(setting("PointerAcceleration"))
                slider("AccelerationStrength", model.t("Acceleration strength", "Сила ускорения"), value: model.number("AccelerationStrength"), range: 0...2, unit: "%", multiplier: 100).disabled(!model.draft.flags["PointerAcceleration", default: false]).modifier(setting("AccelerationStrength"))
                slider("PointerSmoothing", model.t("Stabilization", "Сглаживание"), value: model.number("PointerSmoothing"), range: 0...80, unit: model.t("ms", "мс")).modifier(setting("PointerSmoothing"))
            }.disabled(!model.draft.touchEnabled)
            Section(model.t("Scrolling", "Прокрутка")) {
                slider("ScrollSpeed", model.t("Speed", "Скорость"), value: model.number("ScrollSpeed"), range: 0.1...4, unit: "×").modifier(setting("ScrollSpeed"))
                Toggle(model.t("Natural scrolling", "Естественная прокрутка"), isOn: model.flag("NaturalScrolling")).modifier(setting("NaturalScrolling"))
                Toggle(model.t("Momentum", "Инерция"), isOn: model.flag("ScrollInertia")).modifier(setting("ScrollInertia"))
                Toggle(model.t("Acceleration", "Ускорение"), isOn: model.flag("ScrollAcceleration")).modifier(setting("ScrollAcceleration"))
                slider("ScrollAccelerationStrength", model.t("Acceleration strength", "Сила ускорения"), value: model.number("ScrollAccelerationStrength"), range: 0...2, unit: "%", multiplier: 100).disabled(!model.draft.flags["ScrollAcceleration", default: false]).modifier(setting("ScrollAccelerationStrength"))
            }.disabled(!model.draft.touchEnabled)
            Section { Button(model.t("Restore recommended touch settings", "Вернуть рекомендуемые настройки касаний")) { model.resetTouch() }.modifier(setting("resetTouch")) }
        }
    }
    private var gesturesPage: some View {
        Group {
            Section(model.t("Taps", "Касания")) {
                Toggle(model.t("Tap to click", "Касание для щелчка"), isOn: model.flag("TapToClick")).modifier(setting("TapToClick"))
                actionPicker(model.t("One finger", "Один палец"), value: model.command("TapAction")).modifier(setting("TapAction"))
                actionPicker(model.t("Two fingers", "Два пальца"), value: model.command("TwoFingerTapAction")).modifier(setting("TwoFingerTapAction"))
                Toggle(model.t("Double tap and hold to drag", "Двойное касание с удержанием для перетаскивания"), isOn: model.flag("TapToDrag")).disabled(model.draft.commands["TapAction"] != 1).modifier(setting("TapToDrag"))
            }
            Section(model.t("Swipes", "Смахивания")) {
                Toggle(model.t("Two finger swipes", "Смахивания двумя пальцами"), isOn: model.flag("PageSwipes")).modifier(setting("PageSwipes"))
                actionPicker(model.t("Swipe right", "Вправо"), value: model.command("SwipeRightAction")).modifier(setting("SwipeRightAction"))
                actionPicker(model.t("Swipe left", "Влево"), value: model.command("SwipeLeftAction")).modifier(setting("SwipeLeftAction"))
            }
            Section(model.t("Zoom and rotation", "Масштаб и поворот")) {
                Toggle(model.t("Pinch to zoom", "Масштаб щипком"), isOn: model.flag("PinchToZoom")).modifier(setting("PinchToZoom"))
                Text(model.t("Uses application zoom shortcuts. Zoom changes in steps.", "Используются сочетания клавиш приложения. Масштаб меняется ступенчато.")).font(.caption).foregroundStyle(.secondary)
                Toggle(model.t("Rotate documents in Preview", "Поворачивать документы в Просмотре"), isOn: model.flag("Rotate")).modifier(setting("Rotate"))
            }
        }.disabled(!model.draft.touchEnabled)
    }
    private var penPage: some View {
        Group {
            Section(model.t("Stability", "Устойчивость")) {
                slider("hoverSmoothing", model.t("Above the tablet", "Над поверхностью"), value: $model.draft.hoverSmoothing, range: 0...80, unit: model.t("ms", "мс")).modifier(setting("hoverSmoothing"))
                slider("contactSmoothing", model.t("While touching", "При касании"), value: $model.draft.contactSmoothing, range: 0...40, unit: model.t("ms", "мс")).modifier(setting("contactSmoothing"))
                Text(model.t("Higher values steady the pointer but add a little delay. Zero turns stabilization off.", "Большие значения уменьшают дрожание, добавляя небольшую задержку. Ноль отключает сглаживание.")).font(.caption).foregroundStyle(.secondary)
            }
            Section(model.t("Pressure", "Нажим")) {
                Toggle(model.t("Use pen pressure", "Использовать нажим пера"), isOn: $model.draft.pressureEnabled).modifier(setting("pressureEnabled"))
                slider("tipThreshold", model.t("Tip activation", "Порог кончика"), value: $model.draft.tipThreshold, range: 0...100, unit: "%").modifier(setting("tipThreshold"))
                slider("eraserThreshold", model.t("Eraser activation", "Порог ластика"), value: $model.draft.eraserThreshold, range: 0...100, unit: "%").modifier(setting("eraserThreshold"))
                Text(model.t("The eraser and pressure require support in the drawing application.", "Ластик и нажим работают в приложениях с поддержкой планшета.")).font(.caption).foregroundStyle(.secondary)
            }
            Section { Button(model.t("Test pen and touch", "Проверить перо и касания")) { model.action("test") } }
        }
    }
    private var buttonsPage: some View {
        Group {
            Section(model.t("Pen buttons", "Кнопки пера")) {
                ForEach(model.draft.penButtons.indices, id: \.self) { index in
                    actionPicker(index == 0 ? model.t("Lower button", "Нижняя кнопка") : model.t("Upper button", "Верхняя кнопка"), value: model.button(index, pen: true), custom: true).modifier(setting("penButton\(index)"))
                }
            }
            Section(model.t("Tablet buttons", "Кнопки планшета")) {
                ForEach(model.draft.bodyButtons.indices, id: \.self) { index in
                    actionPicker(model.t("Button", "Кнопка") + " \(index + 1)", value: model.button(index, pen: false), custom: true).modifier(setting("bodyButton\(index)"))
                }
                if model.draft.rotation == 270 { Label(model.t("Disabled when tablet buttons are at the top.", "Отключены в вертикальном положении с кнопками сверху."), systemImage: "info.circle").font(.caption).foregroundStyle(.secondary) }
            }.disabled(model.draft.rotation == 270)
        }
    }
    private var diagnosticsPage: some View {
        Group {
            Section(model.t("Status", "Состояние")) {
                statusRow(model.t("Tablet", "Планшет"), ok: model.draft.connected).modifier(setting("connected"))
                statusRow(model.t("Input Monitoring", "Мониторинг ввода"), ok: model.draft.inputGranted).modifier(setting("inputGranted"))
                statusRow(model.t("Accessibility", "Универсальный доступ"), ok: model.draft.accessibilityGranted).modifier(setting("accessibilityGranted"))
                LabeledContent(model.t("Version", "Версия"), value: model.draft.version).textSelection(.enabled).modifier(setting("version"))
                LabeledContent(model.t("Architecture", "Архитектура"), value: "Apple Silicon · ARM64").modifier(setting("architecture"))
            }
            Section(model.t("Troubleshooting", "Поиск неполадок")) {
                Button { model.diagnostics() } label: { Label(model.t("Save diagnostics…", "Сохранить диагностику…"), systemImage: "square.and.arrow.down") }.modifier(setting("diagnostics"))
                Button { model.action("logs") } label: { Label(model.t("Open logs folder", "Открыть папку журналов"), systemImage: "folder") }.modifier(setting("logs"))
                Button { model.action("test") } label: { Label(model.t("Test pen and touch", "Проверить перо и касания"), systemImage: "pencil.and.outline") }.modifier(setting("test"))
                Text(model.t("The report includes version, permissions, tablet name and recent logs. It is saved locally.", "Отчёт содержит версию, разрешения, название планшета и последние журналы. Файл сохраняется на Mac.")).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
    private var generalPage: some View {
        Group {
            Section(model.t("Appearance", "Оформление")) {
                Picker(model.t("Language", "Язык"), selection: Binding(get: { model.draft.language }, set: { model.setLanguage($0) })) {
                    Text("English").tag("en")
                    Text("Русский").tag("ru")
                }.modifier(setting("language"))
                Text(model.t("Settings language changes immediately. Advanced windows use the new language after restarting the app.", "Язык настроек меняется сразу. Расширенные окна сменят язык после перезапуска приложения."))
                    .font(.caption).foregroundStyle(.secondary)
                LabeledContent(model.t("Theme", "Тема"), value: model.t("Follows macOS", "Как в macOS")).modifier(setting("theme"))
            }
            Section(model.t("Background operation", "Фоновая работа")) {
                Label(model.t("The driver stays in the menu bar when settings are closed.", "После закрытия настроек драйвер остаётся в строке меню."), systemImage: "menubar.rectangle").modifier(setting("background"))
                Text(model.t("Use the tablet icon to reopen settings or quit the driver.", "Через значок планшета можно открыть настройки или выйти из драйвера.")).font(.caption).foregroundStyle(.secondary)
            }
            Section(model.t("Advanced", "Дополнительно")) {
                Button(model.t("Open advanced settings…", "Открыть расширенные настройки…")) { model.action("advanced") }.modifier(setting("advanced"))
                Text(model.t("Manual geometry, plugins, custom shortcuts and the complete journal.", "Ручная геометрия, плагины, свои сочетания клавиш и полный журнал.")).font(.caption).foregroundStyle(.secondary)
            }
        }
    }
    private func slider(_ id: String, _ title: String, value: Binding<Double>, range: ClosedRange<Double>, unit: String, multiplier: Double = 1) -> some View {
        NumericSlider(model: model, id: id, title: title, value: value, range: range, unit: unit, multiplier: multiplier)
    }
    private func actionPicker(_ title: String, value: Binding<Int>, custom: Bool = false) -> some View {
        Picker(title, selection: value) {
            if custom && value.wrappedValue == -1 { Text(model.t("Custom assignment", "Своё назначение")).tag(-1) }
            ForEach(0..<10) { action in Text(commandName(action)).tag(action) }
        }
    }
    private func commandName(_ action: Int) -> String {
        let en = ["No action", "Left click", "Right click", "Double click", "Back", "Forward", "Mission Control", "Show desktop", "Previous tab", "Next tab"]
        let ru = ["Без действия", "Левый щелчок", "Правый щелчок", "Двойной щелчок", "Назад", "Вперёд", "Mission Control", "Показать рабочий стол", "Предыдущая вкладка", "Следующая вкладка"]
        return model.russian ? ru[action] : en[action]
    }
    private func statusRow(_ title: String, ok: Bool) -> some View {
        LabeledContent(title) { Label(ok ? model.t("Available", "Доступно") : model.t("Unavailable", "Недоступно"), systemImage: ok ? "checkmark.circle.fill" : "exclamationmark.circle.fill").foregroundStyle(ok ? .green : .orange) }
    }
}
private struct NumericSlider: View {
    @ObservedObject var model: SettingsModel
    let id: String
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let unit: String
    let multiplier: Double
    @FocusState private var editing: Bool
    private var invalid: Bool { model.numericDrafts[id]?.valid == false }
    private var text: Binding<String> {
        Binding(get: { model.numericDrafts[id]?.text ?? BambooNumericInput.format(value, multiplier: multiplier, russian: model.russian) },
                set: { model.editNumber(id, text: $0, value: $value, range: range, multiplier: multiplier) })
    }
    private var sliderValue: Binding<Double> {
        Binding(get: { value }, set: {
            model.numericDrafts.removeValue(forKey: id)
            value = $0
        })
    }
    private var bounds: String {
        BambooNumericInput.format(range.lowerBound, multiplier: multiplier, russian: model.russian) + "–" +
        BambooNumericInput.format(range.upperBound, multiplier: multiplier, russian: model.russian) + " " + unit
    }
    private var validation: String {
        switch model.numericDrafts[id]?.result {
        case .outOfRange: return model.t("Allowed range: ", "Допустимо: ") + bounds
        case .invalid: return model.t("Enter a number with a decimal point or comma.", "Введите число с точкой или запятой.")
        default: return model.t("Complete the number.", "Завершите ввод числа.")
        }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            HStack {
                Text(title)
                Spacer()
                TextField("", text: text)
                    .textFieldStyle(.roundedBorder).multilineTextAlignment(.trailing).monospacedDigit()
                    .frame(width: 110).focused($editing)
                    .accessibilityLabel(title + model.t(", value", ", значение"))
                    .help(bounds)
                    .overlay(RoundedRectangle(cornerRadius: 5).stroke(invalid ? Color.red : Color.clear, lineWidth: 1))
                    .onSubmit { model.finishNumber(id, value: $value); editing = false }
                    .onChange(of: editing) { _, focused in
                        if !focused { model.finishNumber(id, value: $value) }
                    }
                Text(unit).foregroundStyle(.secondary).frame(minWidth: 18, alignment: .leading)
            }
            Slider(value: sliderValue, in: range).accessibilityLabel(title)
            if invalid { Text(validation).font(.caption).foregroundStyle(.red) }
        }.padding(.vertical, 3)
    }
}
private struct SettingAnchor: ViewModifier {
    let id: String
    let selected: Bool
    func body(content: Content) -> some View {
        content.id(id).listRowBackground(selected ? Color.accentColor.opacity(0.12) : Color(nsColor: .controlBackgroundColor))
    }
}
private struct TabletPreview: View {
    @ObservedObject var model: SettingsModel
    var body: some View {
        HStack(spacing: 26) {
            BambooDiagram(settings: model.draft)
                .frame(width: 220, height: 166)
            VStack(alignment: .leading, spacing: 8) {
                Text("Wacom Bamboo").font(.headline)
                Text("CTH-661").foregroundStyle(.secondary)
                Text(model.t("Pen + two-finger touch", "Перо и касания двумя пальцами")).font(.caption).foregroundStyle(.secondary)
            }
        }.frame(maxWidth: .infinity).accessibilityElement(children: .combine)
    }
}
// A vector schematic of CTH-661: four wide keys above the inset pen surface,
// rotated as a whole for the two landscape orientations. Coordinates are drawing units.
private struct BambooDiagram: View {
    let settings: Snapshot
    @Environment(\.colorScheme) private var scheme
    var body: some View {
        Canvas { context, size in
            let angle: CGFloat = settings.rotation == 270 ? 0 : settings.rotation == 180 ? .pi / 2 : -.pi / 2
            let transform = CGAffineTransform(rotationAngle: angle)
            let body = CGRect(x: -50, y: -77, width: 100, height: 154)
            let surface = CGRect(x: -37.5, y: -49, width: 75, height: 118.5)
            var tablet = context
            tablet.translateBy(x: size.width / 2, y: size.height / 2)
            tablet.rotate(by: .radians(Double(angle)))
            let metal = scheme == .dark ? Color(white: 0.32) : Color(white: 0.79)
            let pad = scheme == .dark ? Color(white: 0.22) : Color(white: 0.61)
            tablet.fill(Path(roundedRect: body, cornerRadius: 10), with: .color(metal))
            tablet.stroke(Path(roundedRect: body, cornerRadius: 10), with: .color(.secondary.opacity(0.6)), lineWidth: 0.8)
            let bezel = surface.insetBy(dx: -2, dy: -2)
            tablet.fill(Path(roundedRect: bezel, cornerRadius: 5), with: .color(Color(white: scheme == .dark ? 0.53 : 0.92)))
            tablet.fill(Path(roundedRect: surface, cornerRadius: 4), with: .color(pad))
            for index in 0..<4 {
                let key = CGRect(x: -37.5 + CGFloat(index) * 19, y: -64, width: 18, height: 12)
                tablet.fill(Path(roundedRect: key, cornerRadius: 2), with: .color(Color(white: scheme == .dark ? 0.6 : 0.95)))
                tablet.stroke(Path(roundedRect: key, cornerRadius: 2), with: .color(.black.opacity(0.15)), lineWidth: 0.5)
            }
            tablet.fill(Path(roundedRect: CGRect(x: -0.5, y: -63, width: 1, height: 10), cornerRadius: 0.5), with: .color(.white.opacity(0.85)))
            tablet.fill(Path(roundedRect: CGRect(x: -10, y: 77, width: 20, height: 4), cornerRadius: 1), with: .color(.red.opacity(0.75)))
            guard settings.useMapping else { return }
            // Match TabletMappingPreferences: screen aspect, edge margin, scale, then
            // shift toward the user's bottom edge in the chosen orientation.
            let oriented = surface.applying(transform).offsetBy(dx: size.width / 2, dy: size.height / 2)
            let available = oriented.insetBy(dx: settings.edgeMargin * 75 / 137, dy: settings.edgeMargin * 75 / 137)
            var width = available.width, height = available.height
            let selected = settings.displays.filter { settings.allDisplays || settings.displayIds.contains($0.id) }
            let displays = selected.isEmpty ? settings.displays : selected
            if settings.proportional, let left = displays.map({ $0.x }).min(), let top = displays.map({ $0.y }).min(),
               let right = displays.map({ $0.x + $0.width }).max(), let bottom = displays.map({ $0.y + $0.height }).max(), right > left, bottom > top {
                let ratio = (right - left) / (bottom - top)
                width = min(width, height * ratio); height = width / ratio
            }
            width *= settings.areaScale; height *= settings.areaScale
            let area = CGRect(x: available.midX - width / 2,
                              y: settings.nearUser ? available.maxY - height : available.midY - height / 2,
                              width: width, height: height)
            let path = Path(roundedRect: area, cornerRadius: 3)
            context.fill(path, with: .color(.accentColor.opacity(0.28)))
            context.stroke(path, with: .color(.accentColor), style: StrokeStyle(lineWidth: 1, dash: [3, 2]))
        }
        .accessibilityLabel(settings.rotation == 270 ? "CTH-661: buttons above the working surface" : "CTH-661")
    }
}
private final class SettingsController: NSWindowController, NSWindowDelegate {
    let model: SettingsModel
    func clearFieldFocus() { window?.makeFirstResponder(nil) }
    init(_ snapshot: Snapshot) {
        model = SettingsModel(snapshot)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1000, height: 760), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = snapshot.language == "ru" ? "Настройки планшета" : "Tablet Settings"
        window.titlebarAppearsTransparent = false
        window.toolbarStyle = .unifiedCompact
        window.isReleasedWhenClosed = false
        window.contentViewController = NSHostingController(rootView: SettingsView(model: model))
        window.minSize = NSSize(width: 850, height: 600)
        window.setFrameAutosaveName("BambooNativeSettings")
        window.center()
        super.init(window: window)
        window.delegate = self
    }
    required init?(coder: NSCoder) { fatalError("Not used") }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if model.busy { NSSound.beep(); return false }
        if model.dirty {
            let alert = NSAlert()
            alert.messageText = model.t("Discard unsaved changes?", "Отменить несохранённые изменения?")
            alert.addButton(withTitle: model.t("Keep editing", "Продолжить настройку"))
            alert.addButton(withTitle: model.t("Discard", "Отменить изменения"))
            if alert.runModal() != .alertSecondButtonReturn { return false }
            model.discard()
        }
        sender.orderOut(nil)
        return false // Retain one window and keep the menu-bar driver alive.
    }
}
// This AppKit-hosted SwiftUI window does not have a SwiftUI App/Scene commands
// tree. Supply standard responder-chain edit commands, including Command-A/V.
private final class NativeMenuActions: NSObject {
    static let shared = NativeMenuActions()
    @objc func quit(_ sender: Any?) { requestNativeQuit() }
}
private func requestNativeQuit() {
    if let controller = settingsController { controller.model.action("quit") }
    else { emit(["action": "quit"]) }
}
private func installEditingMenu(russian: Bool) {
    let root = NSApp.mainMenu ?? NSMenu()
    // The hidden Eto form does not install an application menu for this window.
    // Keep Quit in AppKit's responder path and route it through the managed host
    // so its Terminating handlers dispose the daemon and tray indicator.
    let appMenu: NSMenu
    if let first = root.items.first?.submenu { appMenu = first }
    else {
        appMenu = NSMenu(title: "OpenTabletDriver")
        let appItem = NSMenuItem(title: "OpenTabletDriver", action: nil, keyEquivalent: "")
        appItem.submenu = appMenu; root.insertItem(appItem, at: 0)
    }
    let quitID = NSUserInterfaceItemIdentifier("BambooQuit")
    if let item = appMenu.items.first(where: { $0.identifier == quitID }) {
        item.title = russian ? "Завершить OpenTabletDriver" : "Quit OpenTabletDriver"
    } else {
        let item = NSMenuItem(title: russian ? "Завершить OpenTabletDriver" : "Quit OpenTabletDriver",
                              action: #selector(NativeMenuActions.quit(_:)), keyEquivalent: "q")
        item.identifier = quitID; item.target = NativeMenuActions.shared
        item.keyEquivalentModifierMask = [.command]
        appMenu.addItem(item)
    }
    NSApp.mainMenu = root
    func hasSelectAll(_ menu: NSMenu) -> Bool {
        menu.items.contains { $0.action == #selector(NSText.selectAll(_:)) || ($0.submenu.map(hasSelectAll) ?? false) }
    }
    guard !hasSelectAll(root) else { return }
    let edit = NSMenu(title: russian ? "Правка" : "Edit")
    for (english, local, selector, key) in [
        ("Undo", "Отменить", "undo:", "z"),
        ("Redo", "Повторить", "redo:", "Z"),
        ("Cut", "Вырезать", "cut:", "x"),
        ("Copy", "Копировать", "copy:", "c"),
        ("Paste", "Вставить", "paste:", "v"),
        ("Select All", "Выделить всё", "selectAll:", "a")
    ] {
        let item = NSMenuItem(title: russian ? local : english, action: Selector(selector), keyEquivalent: key.lowercased())
        item.keyEquivalentModifierMask = key == "Z" ? [.command, .shift] : [.command]
        edit.addItem(item) // nil target dispatches to the current native text editor.
    }
    let entry = NSMenuItem(title: edit.title, action: nil, keyEquivalent: "")
    entry.submenu = edit; root.addItem(entry); NSApp.mainMenu = root
}
@_cdecl("BambooSettingsShow")
public func bambooSettingsShow(_ text: UnsafePointer<CChar>?, _ callback: (@convention(c) (UnsafePointer<CChar>?) -> Void)?) -> Int32 {
    guard Thread.isMainThread, let text, let callback,
          let data = String(cString: text).data(using: .utf8), let snapshot = try? JSONDecoder().decode(Snapshot.self, from: data) else { return 0 }
    commandHandler = callback
    if let controller = settingsController {
        if !controller.model.dirty && !controller.model.busy { controller.model.update(snapshot) }
    } else { settingsController = SettingsController(snapshot) }
    installEditingMenu(russian: snapshot.language == "ru")
    settingsController?.showWindow(nil)
    settingsController?.window?.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
    settingsController?.clearFieldFocus()
    DispatchQueue.main.async { settingsController?.clearFieldFocus() }
    return 1
}
@_cdecl("BambooSettingsResult")
public func bambooSettingsResult(_ text: UnsafePointer<CChar>?) {
    guard let text else { return }
    let copy = String(cString: text)
    DispatchQueue.main.async { settingsController?.model.receive(copy) }
}

@_cdecl("BambooSettingsAttach")
public func bambooSettingsAttach(_ callback: (@convention(c) (UnsafePointer<CChar>?) -> Void)?) {
    commandHandler = callback
    if activationObserver == nil {
        activationObserver = NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { _ in
            // Becoming active precedes the existing window becoming key. Do not
            // reopen it (and steal a field click) during that transition.
            if NSApp.keyWindow == nil && settingsController?.window?.isVisible != true { emit(["action": "open"]) }
        }
    }
}
@_cdecl("BambooSettingsSave")
public func bambooSettingsSave() -> Int32 {
    guard Thread.isMainThread, let controller = settingsController, controller.window?.isKeyWindow == true else { return 0 }
    if controller.model.dirty && !controller.model.busy { controller.model.save() }
    return 1
}

@_cdecl("BambooSettingsQuit")
public func bambooSettingsQuit() -> Int32 {
    guard Thread.isMainThread else { return 0 }
    requestNativeQuit()
    return 1
}
