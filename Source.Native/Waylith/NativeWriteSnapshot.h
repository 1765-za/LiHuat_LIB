#pragma once
#include <windows.h>
#include <atomic>
#include <memory>
#include "EntryWriteIndex.h"

namespace lihuat {
class NativeWriteSnapshot {
    struct Snapshot {
        EntryWriteIndex ranges;
        Snapshot(const EntryWriteIndex& source, bool persistent) : ranges(source) {
            if (persistent) source.AddStorageTo(ranges);
            ranges.Add(this, sizeof(*this));
            ranges.Seal();
        }
    };
    std::atomic<uintptr_t> published{0};
    mutable std::atomic<uint32_t> readers[2]{};

    void Leave(uintptr_t slot) const {
        if (readers[slot].fetch_sub(1, std::memory_order_seq_cst) == 1)
            WakeByAddressAll(const_cast<std::atomic<uint32_t>*>(&readers[slot]));
    }
public:
    // A registry serializes its writers. Readers must never wait on an allocator-owning writer.
    void Publish(const EntryWriteIndex& source, bool persistent = true) {
        auto next = std::make_unique<Snapshot>(source, persistent);
        uintptr_t slot = (published.load(std::memory_order_seq_cst) & 1) ^ 1;
        uintptr_t previous = published.exchange(reinterpret_cast<uintptr_t>(next.release()) | slot, std::memory_order_seq_cst);
        auto retired = reinterpret_cast<Snapshot*>(previous & ~uintptr_t{1});
        if (!retired) return;
        auto& pending = readers[previous & 1];
        uint32_t count;
        while ((count = pending.load(std::memory_order_seq_cst)) != 0)
            WaitOnAddress(&pending, &count, sizeof(count), INFINITE);
        delete retired;
    }

    bool Overlaps(const void* address, size_t length) const {
        for (;;) {
            uintptr_t version = published.load(std::memory_order_seq_cst);
            uintptr_t slot = version & 1;
            readers[slot].fetch_add(1, std::memory_order_seq_cst);
            if (version != published.load(std::memory_order_seq_cst)) { Leave(slot); continue; }
            auto snapshot = reinterpret_cast<const Snapshot*>(version & ~uintptr_t{1});
            bool found = snapshot && (snapshot->ranges.OwnsStorage(address, length) || snapshot->ranges.Overlaps(address, length));
            Leave(slot);
            return found;
        }
    }
};
}
