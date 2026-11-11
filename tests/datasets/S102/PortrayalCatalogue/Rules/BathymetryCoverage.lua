-- BathymetryCoverage.lua
-- S-102 depth shading rules following the official S-102 Portrayal Catalogue
-- Lua API convention (S-100 Edition 5.2 Portrayal Model).
--
-- Entry point: BathymetryCoverage(feature, featurePortrayal, contextParameters)
--
-- Drawing instruction format:
--   CoverageColor:TOKEN,transparency;LookupEntry:label,min,max,intervalType
-- Interval types:
--   ltSemiInterval  = (-inf, max)
--   geLtInterval    = [min, max)
--   geSemiInterval  = [min, +inf)

function BathymetryCoverage(feature, featurePortrayal, contextParameters)

    local fourShades    = contextParameters.FourShades
    local safetyContour = contextParameters.SafetyContour
    local shallowContour = contextParameters.ShallowContour
    local deepContour   = contextParameters.DeepContour

    -- Intertidal: depth < 0
    featurePortrayal:AddInstructions('CoverageColor:DEPIT,0;LookupEntry:Intertidal,,0,ltSemiInterval')

    if fourShades then
        -- Four-shade mode
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPVS,0;LookupEntry:Shallow Water,0,' .. shallowContour .. ',geLtInterval;CoverageFill:depth')
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPMS,0;LookupEntry:Medium-Shallow Water,' .. shallowContour .. ',' .. safetyContour .. ',geLtInterval')
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPMD,0;LookupEntry:Medium-Deep Water,' .. safetyContour .. ',' .. deepContour .. ',geLtInterval')
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPDW,0;LookupEntry:Deep Water,' .. deepContour .. ',,geSemiInterval')
    else
        -- Two-shade mode
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPVS,0;LookupEntry:Shallow Water,0,' .. safetyContour .. ',geLtInterval;CoverageFill:depth')
        featurePortrayal:AddInstructions(
            'CoverageColor:DEPDW,0;LookupEntry:Deep Water,' .. safetyContour .. ',,geSemiInterval')
    end
end
