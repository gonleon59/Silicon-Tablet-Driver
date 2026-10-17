import Foundation

@main
struct NumericInputTests {
    struct Failure: Error { let message: String }
    static func expect(_ condition: Bool, _ message: String) throws {
        if !condition { throw Failure(message: message) }
    }
    static func main() throws {
        let speed = 0.1...4.0
        try expect(BambooNumericInput.parse("1,25", range: speed) == .valid(1.25), "Comma decimal must work")
        try expect(BambooNumericInput.parse(" 1.25 ", range: speed) == .valid(1.25), "Point and surrounding whitespace must work")
        try expect(BambooNumericInput.parse(".5", range: speed) == .valid(0.5), "Leading decimal must work")
        for text in ["", " ", ".", ",", "-", "+", "-.", "12,"] {
            try expect(BambooNumericInput.parse(text, range: 0...100) == .incomplete, "Partial input must stay incomplete: \(text)")
        }
        try expect(BambooNumericInput.parse("12,", range: 0...100, finalizing: true) == .valid(12), "A trailing separator can finish on blur/Return")
        try expect(BambooNumericInput.parse("", range: 0...100, finalizing: true) == .incomplete, "Empty input must never become zero")
        for text in ["NaN", "inf", "1e2", "12,34.5", "1 000", "35ms", "abc"] {
            try expect(BambooNumericInput.parse(text, range: 0...100) == .invalid, "Reject malformed input: \(text)")
        }
        try expect(BambooNumericInput.parse("75,5", range: 0.3...1, multiplier: 100) == .valid(0.755), "Percent input must scale once")
        try expect(BambooNumericInput.parse("200", range: 0...2, multiplier: 100) == .valid(2), "Percent maximum must work")
        try expect(BambooNumericInput.parse("201", range: 0...2, multiplier: 100) == .outOfRange, "Reject values above the displayed maximum")
        try expect(BambooNumericInput.parse("-1", range: 0...80) == .outOfRange, "Reject negative smoothing")
        try expect(BambooNumericInput.parse("0", range: 0...80) == .valid(0), "Allow explicit zero where supported")
        try expect(BambooNumericInput.parse("0", range: speed) == .outOfRange, "Keep distinct setting minima")
        try expect(BambooNumericInput.parse("1", range: speed, multiplier: 0) == .invalid, "Guard multiplier")
        try expect(BambooNumericInput.format(0.8500000238418579, multiplier: 100, russian: true) == "85", "Hide stored float noise in percent display")
        try expect(BambooNumericInput.format(1.700000047683716, russian: false) == "1.7", "Hide stored float noise in speed display")
        try expect(BambooNumericInput.format(1.25, russian: true) == "1,25" && BambooNumericInput.format(1.25, russian: false) == "1.25", "Use the chosen UI decimal separator")
        let invalid = NumericDraft(text: "999", range: speed, multiplier: 1)
        try expect(!invalid.valid && invalid.text == "999" && invalid.result == .outOfRange, "An invalid draft must retain the actual typed text")
        print("Numeric input: 18 scenarios passed (including partial/malformed input cases).")
    }
}
