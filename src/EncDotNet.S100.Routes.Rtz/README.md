# EncDotNet.S100.Routes.Rtz

`EncDotNet.S100.Routes.Rtz` reads and writes RTZ route plans, the route
exchange format of IEC 61174 Annex S that ECDIS and route planning tools use.
It handles RTZ 1.0 (IEC 61174 Ed. 4.0:2015), 1.1 (the STM extended Annex S)
and 1.2 (IEC PAS 61174-1:2021).

The package depends only on `EncDotNet.S100.Core`. RTZ isn't an S-100
product, so it lives beside the S-421 package rather than in it. A tool that
reads both formats installs both.

## Install

```bash
dotnet add package EncDotNet.S100.Routes.Rtz
```

## Read a route

```csharp
using EncDotNet.S100.Routes.Rtz;

var route = RtzReader.Read("Nab to Southampton.rtz");
Console.WriteLine($"{route.Info.RouteName} (RTZ {route.Version}), {route.Waypoints.Count} waypoints");

// A waypoint's leg is the leg directed to it, so leg i of the route is the
// leg of waypoint i + 1. GetEffectiveLeg applies the defaultWaypoint values.
for (var i = 1; i < route.Waypoints.Count; i++)
{
    var leg = route.GetEffectiveLeg(i);
    Console.WriteLine($"Leg {i}: XTD {leg?.PortsideXtd} / {leg?.StarboardXtd} NM, draught {leg?.MaxDraught} m");
}

// The calculated table wins over the manual one where both are present.
if (route.GetPlanningSchedule() is { } schedule)
    Console.WriteLine($"Departs {schedule.ElementFor(route.Waypoints[0].Id)?.Departure:u}");
```

To recognise a file before reading it, pass its first few kilobytes to
`RtzVersions.Sniff`. It returns the version the file declares, or `null` when
the file isn't RTZ.

## Write a route

```csharp
RtzWriter.Write(route, "Nab to Southampton.rtz", RtzVersions.V1_0);
```

The writer uses the target version's spelling (`sheduleElement` in 1.0,
`scheduleElement` later), time format (`xsd:time` windows in 1.0,
`xsd:duration` later) and extension layout.

## What round-trips

The model names every attribute in the RTZ schema. Everything else is kept:

- Attributes the model doesn't know, or can't parse, are kept in each
  element's `ExtraAttributes`.
- The children of every `extensions` element are kept as `XElement`s, with
  the root's namespace prefixes, so a vendor extension (such as STM's
  `RouteInfoEx`) is written back unchanged.

A route read and written in the same version keeps its content. Writing
another version changes only what the versions differ in: a bare 1.0
extension element is wrapped in an `extension` element (manufacturer
"unknown") for 1.1 and 1.2, leg extensions (a 1.2 addition) are left out
of 1.0 and 1.1, and 1.2 gives every waypoint a `revision`.

This package is a data exchange library. It isn't for navigation.
