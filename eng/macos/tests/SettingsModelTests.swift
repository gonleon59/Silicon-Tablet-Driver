import AppKit
import SwiftUI

@main
struct SettingsModelTests {
    static var checks = 0
    static func expect(_ condition: @autoclosure () -> Bool, _ description: String) {
        checks += 1
        precondition(condition(), description)
    }
    static func fixture(rotation: Int = 270, cropped: Bool = true) -> Snapshot {
        Snapshot(profile: "Wacom CTH-661", version: "test", language: "en", profileToken: "profile", settingsToken: "settings",
                 connected: true, hasProfile: true, inputGranted: true, accessibilityGranted: true, displays: [],
                 useMapping: cropped, hasManualArea: false, mappingEdited: false, allDisplays: true, displayIds: [], rotation: rotation,
                 areaScale: 0.85, edgeMargin: 8, nearUser: true, proportional: true, horizontalSideMargin: 4.5, horizontalTopBottomMargin: 5, touchEnabled: true,
                 touch: [:], flags: [:], commands: [:], hoverSmoothing: 35, contactSmoothing: 12,
                 tipThreshold: 1, eraserThreshold: 1, pressureEnabled: true, penButtons: [], bodyButtons: [])
    }
    static func savedReply(_ model: SettingsModel) throws -> String {
        var saved = model.draft; saved.mappingEdited = false
        let settings = try JSONSerialization.jsonObject(with: JSONEncoder().encode(saved))
        return String(data: try JSONSerialization.data(withJSONObject: ["success": true, "saved": true, "settings": settings]), encoding: .utf8)!
    }
    static func main() throws {
        for rotation in [0, 180] {
            let model = SettingsModel(fixture())
            model.orientation.wrappedValue = rotation
            expect(model.draft.rotation == rotation && model.draft.mappingEdited, "Orientation is a mapping edit")
            expect(!model.draft.useMapping && !model.comfortableAreaAvailable, "Landscape cannot enable a crop")
            model.comfortableArea.wrappedValue = true
            expect(!model.draft.useMapping, "Hidden crop cannot be activated in landscape")
            model.save()
            expect(model.busy && !model.canEditOrientation, "Saving temporarily blocks orientation")
            model.receive(try savedReply(model))
            expect(!model.busy && !model.dirty && model.canEditOrientation, "Saved full-area mode leaves orientation usable")
            model.orientation.wrappedValue = 270
            expect(model.draft.useMapping && model.comfortableAreaAvailable && model.dirty, "Can switch back to vertical after saving")
        }
        var manualSnapshot = fixture(cropped: false); manualSnapshot.hasManualArea = true
        let manual = SettingsModel(manualSnapshot)
        expect(manual.canEditOrientation, "Manual geometry cannot lock the orientation picker")
        manual.orientation.wrappedValue = 0
        manual.save(); manual.receive("{\"success\":false,\"error\":\"test rejection\"}")
        expect(manual.dirty && !manual.busy && manual.canEditOrientation, "A rejected save preserves edits and releases controls")
        manual.discard()
        expect(!manual.dirty && manual.draft.rotation == 270 && manual.canEditOrientation, "Revert restores the draft without locking the picker")
        manual.useFullArea()
        expect(!manual.hasManualArea && !manual.draft.useMapping && manual.draft.mappingEdited, "Explicit full-area reset replaces an old manual crop")
        let fullVertical = SettingsModel(fixture())
        fullVertical.comfortableArea.wrappedValue = false
        expect(!fullVertical.draft.useMapping && fullVertical.draft.mappingEdited, "Turning off crop requests full geometry")
        fullVertical.save(); fullVertical.receive(try savedReply(fullVertical))
        expect(fullVertical.canEditOrientation && !fullVertical.draft.useMapping, "Saved uncropped vertical remains editable")
        let invalid = SettingsModel(fixture())
        invalid.editNumber("areaScale", text: "-", value: Binding(get: { invalid.draft.areaScale }, set: { invalid.draft.areaScale = $0 }), range: 0.3...1, multiplier: 100)
        expect(invalid.hasInvalidNumbers, "Unfinished crop input is invalid")
        invalid.orientation.wrappedValue = 0
        expect(!invalid.hasInvalidNumbers, "Hidden crop input cannot block full-area saving")
        invalid.editNumber("PointerSpeed", text: "-", value: invalid.number("PointerSpeed"), range: 0.1...10, multiplier: 1)
        invalid.orientation.wrappedValue = 180
        expect(invalid.hasInvalidNumbers, "Unrelated invalid input remains visible to validation")
        let search = SettingsModel(fixture(rotation: 0, cropped: false))
        search.search = "area"
        expect(!search.searchResults.contains { $0.id == "areaScale" || $0.id == "useMapping" }, "Landscape search omits unavailable crop controls")
        print("Settings model: \(checks) checks passed (orientation, full-area saves, rejection, Revert and numeric drafts).")
    }
}
