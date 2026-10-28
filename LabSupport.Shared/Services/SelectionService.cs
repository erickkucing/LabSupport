using System;
using System.Collections.Generic;
using System.Linq;
using LabSupport.Shared.Models;

namespace LabSupport.Shared.Services;

public class RoomSummaryInfo
{
    public string RoomName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int OnlineCount { get; set; }
    public int SelectedCount { get; set; }
    public bool IsFullySelected => TotalCount > 0 && SelectedCount == TotalCount;
    public bool IsPartiallySelected => SelectedCount > 0 && SelectedCount < TotalCount;
}

public static class SelectionService
{
    public static void SelectRoom<T>(IEnumerable<T> computers, string roomName) where T : ComputerInfo
    {
        if (computers == null || string.IsNullOrWhiteSpace(roomName)) return;

        foreach (var c in computers)
        {
            string room = string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName;
            if (string.Equals(room, roomName, StringComparison.OrdinalIgnoreCase))
            {
                c.IsSelected = true;
            }
        }
    }

    public static void DeselectRoom<T>(IEnumerable<T> computers, string roomName) where T : ComputerInfo
    {
        if (computers == null || string.IsNullOrWhiteSpace(roomName)) return;

        foreach (var c in computers)
        {
            string room = string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName;
            if (string.Equals(room, roomName, StringComparison.OrdinalIgnoreCase))
            {
                c.IsSelected = false;
            }
        }
    }

    public static void SelectMultipleRooms<T>(IEnumerable<T> computers, IEnumerable<string> roomNames) where T : ComputerInfo
    {
        if (computers == null || roomNames == null) return;

        var set = new HashSet<string>(roomNames, StringComparer.OrdinalIgnoreCase);
        foreach (var c in computers)
        {
            string room = string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName;
            if (set.Contains(room))
            {
                c.IsSelected = true;
            }
        }
    }

    public static void SelectAll<T>(IEnumerable<T> computers, bool select) where T : ComputerInfo
    {
        if (computers == null) return;

        foreach (var c in computers)
        {
            c.IsSelected = select;
        }
    }

    public static void InvertSelection<T>(IEnumerable<T> computers) where T : ComputerInfo
    {
        if (computers == null) return;

        foreach (var c in computers)
        {
            c.IsSelected = !c.IsSelected;
        }
    }

    public static string FormatSelectionSummary<T>(IEnumerable<T> computers) where T : ComputerInfo
    {
        if (computers == null) return "0 PC Terpilih";

        var selected = computers.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return "0 PC Terpilih";

        var byRoom = selected
            .GroupBy(c => string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}");

        return $"🎯 {selected.Count} PC Terpilih ({string.Join(", ", byRoom)})";
    }

    public static List<RoomSummaryInfo> GetRoomSummaries<T>(IEnumerable<T> computers) where T : ComputerInfo
    {
        if (computers == null) return new List<RoomSummaryInfo>();

        return computers
            .GroupBy(c => string.IsNullOrWhiteSpace(c.RoomName) ? "General" : c.RoomName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RoomSummaryInfo
            {
                RoomName = g.Key,
                TotalCount = g.Count(),
                OnlineCount = g.Count(c => c.IsOnline),
                SelectedCount = g.Count(c => c.IsSelected)
            })
            .ToList();
    }
}
