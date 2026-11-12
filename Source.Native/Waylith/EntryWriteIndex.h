#pragma once
#include <algorithm>
#include <cstdint>
#include <vector>

namespace lihuat {
class EntryWriteIndex {
    struct Range { uintptr_t begin, end; };
    std::vector<Range> ranges;
public:
    using RetiredStorage = std::vector<Range>;
    // The caller retains the old allocation until publishing its replacement ownership.
    RetiredStorage Reserve(size_t capacity) {
        if (capacity <= ranges.capacity()) return {};
        RetiredStorage previous;
        previous.reserve((std::max)(capacity, ranges.capacity() * 2));
        previous.assign(ranges.begin(), ranges.end());
        previous.swap(ranges);
        return previous;
    }
    void Clear() { ranges.clear(); }
    void AddStorageTo(EntryWriteIndex& destination) const {
        destination.Add(ranges.data(), ranges.capacity() * sizeof(Range));
    }
    RetiredStorage ReserveAdditional(size_t count) {
        return Reserve(ranges.size() + count);
    }
    RetiredStorage Include(const void* address, size_t length) {
        auto previous = ReserveAdditional(1);
        uintptr_t begin = reinterpret_cast<uintptr_t>(address), end = begin + length;
        auto first = std::lower_bound(ranges.begin(), ranges.end(), begin,
            [](const Range& range, uintptr_t value) { return range.end < value; });
        auto last = first;
        while (last != ranges.end() && last->begin <= end) {
            begin = (std::min)(begin, last->begin); end = (std::max)(end, last->end); ++last;
        }
        auto at = ranges.erase(first, last);
        ranges.insert(at, {begin, end});
        return previous;
    }
    void Add(const void* address, size_t length) {
        uintptr_t begin = reinterpret_cast<uintptr_t>(address);
        if (begin && length && length <= UINTPTR_MAX - begin) ranges.push_back({begin, begin + length});
    }
    void Seal() {
        std::sort(ranges.begin(), ranges.end(), [](const Range& a, const Range& b) { return a.begin < b.begin; });
        size_t used = 0;
        for (const Range& range : ranges) {
            if (used && range.begin <= ranges[used - 1].end)
                ranges[used - 1].end = (std::max)(ranges[used - 1].end, range.end);
            else ranges[used++] = range;
        }
        ranges.resize(used);
    }
    bool Overlaps(const void* address, size_t length) const {
        uintptr_t begin = reinterpret_cast<uintptr_t>(address);
        if (!begin || !length || length > UINTPTR_MAX - begin) return false;
        uintptr_t end = begin + length;
        auto next = std::lower_bound(ranges.begin(), ranges.end(), end,
            [](const Range& range, uintptr_t limit) { return range.begin < limit; });
        return next != ranges.begin() && std::prev(next)->end > begin;
    }
    bool OwnsStorage(const void* address, size_t length) const {
        uintptr_t start = reinterpret_cast<uintptr_t>(address);
        uintptr_t storage = reinterpret_cast<uintptr_t>(ranges.data());
        size_t bytes = ranges.capacity() * sizeof(Range);
        return storage && bytes && start && length && length <= UINTPTR_MAX - start
            && storage < start + length && (start < storage || start - storage < bytes);
    }
};
}
