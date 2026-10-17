import Foundation

@main
struct SettingsSearchTests {
    struct Failure: Error { let message: String }
    static func expect(_ condition: Bool, _ message: String) throws {
        if !condition { throw Failure(message: message) }
    }
    static func ids(_ query: String, displays: [String] = []) -> Set<String> {
        Set(SettingsSearchIndex.search(query, displayNames: displays).map(\.id))
    }
    static func main() throws {
        // The index has no UI-language argument: both languages are always available.
        try expect(ids("scroll speed") == ["ScrollSpeed"], "English multi-word query should isolate scroll speed")
        try expect(ids("скорость прокрутки") == ["ScrollSpeed"], "Russian query should find the same control")
        try expect(ids("ERASER").contains("eraserThreshold") && ids("ЛАСТИК").contains("eraserThreshold"), "Pen/eraser search must cross languages and case")
        try expect(ids("сглаживание пера") == ["hoverSmoothing", "contactSmoothing"], "Both pen stabilization controls must be discoverable")
        try expect(ids("  DOUBLE   tap hold ") == ["TapToDrag"], "Whitespace and token order should not affect drag search")
        try expect(ids("вперёд") == ids("вперед"), "Russian yo/e folding must be stable")
        try expect(ids("Odyssey", displays: ["LM27-P501", "Odyssey G40B"]) == ["displays"], "Connected display names must be searchable")
        try expect(ids("следующая вкладка").contains("bodyButton0") && ids("next tab").contains("TapAction"), "Available action choices must also be searchable")
        try expect(ids("qzx987missing").isEmpty && ids(" \n\t").isEmpty, "Unknown/empty queries must produce no matches")
        try expect(ids("language") == ["language"] && ids("русский") == ["language"] && ids("English") == ["language"], "Language selection must be discoverable in both languages")
        try expect(Set(SettingsSearchIndex.items.map(\.id)).count == SettingsSearchIndex.items.count, "Duplicate target IDs break navigation")
        try expect(Set(SettingsSearchIndex.items.map(\.page)) == Set(SettingsPage.allCases), "Every settings page must be indexed")
        print("Native search: 12 checks passed; \(SettingsSearchIndex.items.count) indexed settings across \(SettingsPage.allCases.count) pages.")
    }
}
