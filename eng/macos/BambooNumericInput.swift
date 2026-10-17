import Foundation

// Keep parsing separate from SwiftUI so partial edits never become zero/default values.
enum NumericInputResult: Equatable {
    case valid(Double)
    case incomplete
    case invalid
    case outOfRange
}
enum BambooNumericInput {
    static func parse(_ text: String, range: ClosedRange<Double>, multiplier: Double = 1, finalizing: Bool = false) -> NumericInputResult {
        guard multiplier.isFinite, multiplier > 0 else { return .invalid }
        let token = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        if token.isEmpty || ["-", "+", ".", "-.", "+."].contains(token) { return .incomplete }
        guard token.range(of: "^[+-]?(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)$", options: .regularExpression) != nil else { return .invalid }
        if token.hasSuffix(".") && !finalizing { return .incomplete }
        guard let displayed = Double(token), displayed.isFinite else { return .invalid }
        let value = displayed / multiplier
        guard value.isFinite else { return .invalid }
        guard range.contains(value) else { return .outOfRange }
        return .valid(value)
    }
    static func format(_ value: Double, multiplier: Double = 1, russian: Bool) -> String {
        guard value.isFinite, Float(value).isFinite, multiplier.isFinite, multiplier > 0 else { return "" }
        // Profiles persist float32 values. Their shortest representation avoids exposing
        // float noise such as 0.8500000238 as an apparent user edit to 85 percent.
        let decimal = Decimal(string: String(Float(value)), locale: Locale(identifier: "en_US_POSIX")) ?? Decimal(value)
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: russian ? "ru_RU" : "en_US")
        formatter.numberStyle = .decimal
        formatter.usesGroupingSeparator = false
        formatter.minimumFractionDigits = 0
        formatter.maximumFractionDigits = 8
        return formatter.string(from: NSDecimalNumber(decimal: decimal * Decimal(multiplier))) ?? ""
    }
}
struct NumericDraft {
    var text: String
    let range: ClosedRange<Double>
    let multiplier: Double
    var result: NumericInputResult { BambooNumericInput.parse(text, range: range, multiplier: multiplier) }
    var valid: Bool { if case .valid = result { return true }; return false }
}
