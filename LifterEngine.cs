using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ProfessionalPowerCopyCatalogModern
{
    // ================================================================
    // LifterEngine - full C# port of the "CATIA Lifter Parameters" catvba
    // macro (Lifter Studio). The macro's HTA dashboard + temp-file command
    // protocol is replaced by direct COM calls from the WPF dashboard, but
    // every CATIA operation is a 1:1 port of the original VBA:
    //
    //   StrokeParameterExists      <- StrokeParameterExists
    //   DetectMainBody             <- DetectMainBody
    //   MeasureStrokeOnBody        <- MeasureStrokeOnBody (bbox W, Y direction)
    //   SelectBodyFromUser         <- SelectBodyFromUser (SelectElement2)
    //   LinkStrokeToMainBody       <- LinkStrokeToMainBody (formula per instance)
    //   CreateDraftParameters      <- CreateDraftParameters (Draft rule, max 15 deg)
    //   GetDraftDisplay            <- GetDraftDisplay (formula-first readout)
    //   ReadInstance               <- SendValues (parameter readout)
    //   UpdateInstance             <- UpdateValues (ValuateFromString)
    //   RunBooleanRemove           <- RunBooleanRemoveCore (Copy -> Paste Special
    //                                  As Result -> AddNewRemove)
    //
    // All CATIA access is late-bound (dynamic), so the project only needs the
    // INFITF and MECMOD COM references it already has. Out-of-process COM
    // hands out a fresh RCW per call, so body identity is resolved by name
    // (CATIA keeps body names unique inside a part), replacing the VBA
    // "Is" proxy comparisons.
    // ================================================================

    internal sealed class LifterInstanceSnapshot
    {
        public int Index = 1;
        public string UpperInternalLength = "";
        public string InternalVerticalAngle = "";
        public string InternalHorizontalAngle = "";
        public string HasExternalFace = "false";
        public string UndercutLength = "";
        public string Stroke = "";
        public string Draft = "-";
        public string DraftSource = "";
    }

    internal sealed class LifterPreFlightResult
    {
        public bool Ok;
        public string Error = "";
        public bool StrokeCreated;
        public int Linked;
        public int Drafts;
    }

    internal sealed class LifterRemoveResult
    {
        public bool Ok;
        public string Message = "";
        public string FeatureName = "";
        public string DeadSlot;            // "copy" | "target" | null
    }

    internal static class LifterEngine
    {
        public const string P1 = "UPPER_INTERNAL_LENGTH";
        public const string P2 = "INTERNAL_VERTICAL_ANGLE";
        public const string P3 = "INTERNAL_HORIZONTAL_ANGLE";
        public const string P4 = "HAS_EXTERNAL_FACE";
        public const string P5 = "UNDERCUT_LENGTH";
        public const string PStroke = "STROKE_Distance";
        public const string PDraft = "Draft";
        public const int DraftMaxDeg = 15;                 // injection molding rule 1
        private const string PasteModeNoLink = "CATPrtResultWithOutLink";
        private const string TempSetName = "TEMP_STROKE";

        // ==============================================================
        // Generic late-bound COM helpers
        // ==============================================================

        private static dynamic TryItemByName(dynamic collection, string name)
        {
            if (collection == null || string.IsNullOrEmpty(name)) return null;
            try { return collection.Item(name); } catch { return null; }
        }

        private static dynamic TryItemByIndex(dynamic collection, int index)
        {
            if (collection == null) return null;
            try { return collection.Item(index); } catch { return null; }
        }

        private static int TryCount(dynamic collection)
        {
            if (collection == null) return 0;
            try { return Convert.ToInt32(collection.Count, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        public static string GetName(dynamic catiaObject)
        {
            if (catiaObject == null) return "";
            try { return Convert.ToString(catiaObject.Name, CultureInfo.InvariantCulture) ?? ""; }
            catch { return ""; }
        }

        public static string LeafName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";
            int position = fullName.LastIndexOf('\\');
            if (position >= 0) fullName = fullName.Substring(position + 1);
            position = fullName.LastIndexOf('/');
            if (position >= 0) fullName = fullName.Substring(position + 1);
            return fullName.Trim();
        }

        private static string ReplaceIgnoreCase(string text, string search, string replacement)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search)) return text ?? "";
            int position;
            while ((position = text.IndexOf(search, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, position) + replacement +
                       text.Substring(position + search.Length);
            }
            return text;
        }

        private static dynamic RootParameters(dynamic part)
        {
            try { return part.Parameters; } catch { return null; }
        }

        // ==============================================================
        // Parameter value helpers
        // ==============================================================

        private static string ValueOnly(dynamic parameter)
        {
            try
            {
                string text = Convert.ToString(parameter.ValueAsString, CultureInfo.InvariantCulture) ?? "";
                text = ReplaceIgnoreCase(text, "mm", "");
                text = ReplaceIgnoreCase(text, "deg", "");
                text = text.Replace("\u00B0", "");
                return text.Trim().Replace(',', '.');
            }
            catch { return ""; }
        }

        private static string BooleanText(dynamic parameter)
        {
            try
            {
                return Convert.ToBoolean(parameter.Value, CultureInfo.InvariantCulture) ? "true" : "false";
            }
            catch { }
            try
            {
                return (Convert.ToString(parameter.ValueAsString, CultureInfo.InvariantCulture) ?? "").Trim().ToLowerInvariant();
            }
            catch { }
            return "false";
        }

        private static bool NumericValue(dynamic parameter, out double value)
        {
            value = 0;
            if (parameter == null) return false;
            try
            {
                value = Convert.ToDouble(parameter.Value, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        private static string RelationName(dynamic part, dynamic parameter)
        {
            try
            {
                dynamic parameters = part.Parameters;
                if (parameters == null || parameter == null) return "";
                return Convert.ToString(parameters.GetNameToUseInRelation(parameter), CultureInfo.InvariantCulture) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Direct (non-recursive) parameter lookup inside a parameter set,
        /// with the leaf-name fallback walk of the VBA FindDirect.</summary>
        private static dynamic FindDirect(dynamic parameterSet, string wantedName)
        {
            dynamic direct = null;
            try { direct = parameterSet.DirectParameters; } catch { }
            if (direct == null) return null;

            dynamic hit = TryItemByName(direct, wantedName);
            if (hit != null) return hit;

            int count = TryCount(direct);
            for (int i = 1; i <= count; i++)
            {
                dynamic candidate = TryItemByIndex(direct, i);
                if (candidate == null) continue;
                if (string.Equals(LeafName(GetName(candidate)), wantedName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        // ==============================================================
        // Instance discovery (UNDERCUT_DEPTH + LIFTER_HEAD sets)
        // ==============================================================

        private static bool IsUndercutSet(dynamic parameterSet)
        {
            return FindDirect(parameterSet, P1) != null &&
                   FindDirect(parameterSet, P2) != null &&
                   FindDirect(parameterSet, P3) != null &&
                   FindDirect(parameterSet, P4) != null;
        }

        public static List<dynamic> GetUndercutSets(dynamic part)
        {
            var result = new List<dynamic>();
            try
            {
                dynamic root = part.Parameters.RootParameterSet;
                CollectUndercutSets(root, result);
            }
            catch { }
            return result;
        }

        private static void CollectUndercutSets(dynamic parentSet, List<dynamic> result)
        {
            dynamic childSets = null;
            try { childSets = parentSet.ParameterSets; } catch { }
            int count = TryCount(childSets);
            for (int i = 1; i <= count; i++)
            {
                dynamic child = TryItemByIndex(childSets, i);
                if (child == null) continue;
                if (IsUndercutSet(child)) result.Add(child);
                CollectUndercutSets(child, result);
            }
        }

        public static List<dynamic> GetHeadSets(dynamic part)
        {
            var result = new List<dynamic>();
            try
            {
                dynamic root = part.Parameters.RootParameterSet;
                CollectHeadSets(root, result);
            }
            catch { }
            return result;
        }

        private static void CollectHeadSets(dynamic parentSet, List<dynamic> result)
        {
            dynamic childSets = null;
            try { childSets = parentSet.ParameterSets; } catch { }
            int count = TryCount(childSets);
            for (int i = 1; i <= count; i++)
            {
                dynamic child = TryItemByIndex(childSets, i);
                if (child == null) continue;
                if (FindDirect(child, P5) != null) result.Add(child);
                CollectHeadSets(child, result);
            }
        }

        public static dynamic GetUndercutSet(dynamic part, int instanceIndex)
        {
            if (instanceIndex < 1) instanceIndex = 1;
            List<dynamic> sets = GetUndercutSets(part);
            return instanceIndex <= sets.Count ? sets[instanceIndex - 1] : null;
        }

        public static dynamic GetHeadSet(dynamic part, int instanceIndex)
        {
            if (instanceIndex < 1) instanceIndex = 1;
            List<dynamic> sets = GetHeadSets(part);
            return instanceIndex <= sets.Count ? sets[instanceIndex - 1] : null;
        }

        public static int InstanceCount(dynamic part)
        {
            int count = GetUndercutSets(part).Count;
            int heads = GetHeadSets(part).Count;
            return heads < count ? heads : count;
        }

        // ==============================================================
        // STROKE_Distance (single source of truth)
        // ==============================================================

        public static bool StrokeParameterExists(dynamic part)
        {
            return TryItemByName(RootParameters(part), PStroke) != null;
        }

        public static string GetStrokeText(dynamic part)
        {
            dynamic parameter = TryItemByName(RootParameters(part), PStroke);
            return parameter == null ? "" : ValueOnly(parameter);
        }

        /// <summary>Main-body detection (VBA DetectMainBody) using the SAME
        /// early-bound interop strategy as the main app's PowerCopy lookup
        /// (FindAndSelectReference): typed MECMOD.Part / INFITF.Selection calls,
        /// not late-bound dynamic dispatch. Some document states reject
        /// late-bound calls with E_FAIL while the early-bound path works.</summary>
        public static dynamic DetectMainBody(MECMOD.PartDocument document)
        {
            Exception error;
            return DetectMainBody(document, out error);
        }

        /// <summary>Same as <see cref="DetectMainBody(MECMOD.PartDocument)"/> but
        /// reports the last COM error instead of swallowing it, so the UI can
        /// tell the user whether CATIA is busy or the part has no body.</summary>
        public static dynamic DetectMainBody(MECMOD.PartDocument document, out Exception error)
        {
            error = null;
            try
            {
                MECMOD.Part part = document.Part;
                dynamic body = part.MainBody;
                if (body != null) return body;
            }
            catch (Exception ex) { error = ex; }
            try
            {
                MECMOD.Part part = document.Part;
                dynamic bodies = part.Bodies;
                if (TryCount(bodies) > 0) return bodies.Item(1);
            }
            catch (Exception ex) { error = ex; }

            // Last resort: enumerate the bodies through the typed document
            // Selection.Search - the exact call style FindAndSelectReference
            // uses to find the Power Copy (proven to work on this CATIA).
            try
            {
                INFITF.Selection selection = document.Selection;
                selection.Clear();
                selection.Search("'Part Design'.Body,all");
                if (selection.Count2 > 0)
                {
                    dynamic body = selection.Item2(1).Value;
                    selection.Clear();
                    return body;
                }
                selection.Clear();
            }
            catch (Exception ex) { error = ex; }
            return null;
        }

        /// <summary>Deletes a geometrical set by name using the recorded-macro
        /// Selection.Delete method (VBA DeleteGeometricalSet).</summary>
        private static void DeleteGeometricalSet(dynamic part, string name)
        {
            dynamic document = null;
            try { document = part.Parent; } catch { }
            dynamic selection = null;
            try { selection = document.Selection; } catch { }
            if (selection == null) return;

            while (true)
            {
                dynamic bodies = null;
                try { bodies = part.HybridBodies; } catch { }
                dynamic set = TryItemByName(bodies, name);
                if (set == null) break;
                try
                {
                    selection.Clear();
                    selection.Add(set);
                    selection.Delete();
                    selection.Clear();
                }
                catch { break; }
            }
            try { part.Update(); } catch { }
        }

        /// <summary>Extremum with 3 locked directions - exact port of the VBA
        /// Build_BoxExtremum (minMax: 1 = max, 0 = min).</summary>
        private static dynamic BuildBoxExtremum(dynamic hsf, dynamic refBody, int minMax,
                                                dynamic dir1, dynamic dir2, dynamic dir3)
        {
            dynamic d1 = hsf.AddNewDirection(dir1);
            dynamic d2 = hsf.AddNewDirection(dir2);
            dynamic d3 = hsf.AddNewDirection(dir3);

            dynamic extremum = hsf.AddNewExtremum(refBody, d1, minMax);
            extremum.Direction2 = d2;
            extremum.ExtremumType2 = 1;
            extremum.Direction3 = d3;
            extremum.ExtremumType3 = 1;
            try { extremum.Compute(); } catch { }
            return extremum;
        }

        /// <summary>Measures STROKE_Distance = W of the STANDARD bounding box
        /// (XYZ axes) of the given body, exactly like the VBA
        /// MeasureStrokeOnBody. Creates/updates the root STROKE_Distance
        /// parameter and always removes the TEMP_STROKE set.</summary>
        public static bool MeasureStrokeOnBody(dynamic part, dynamic mainBody)
        {
            Exception error;
            return MeasureStrokeOnBody(part, mainBody, out error);
        }

        /// <summary>Same as <see cref="MeasureStrokeOnBody(object,object)"/> but
        /// reports the COM error behind a failed measurement (busy CATIA,
        /// workbench access, empty body...).</summary>
        public static bool MeasureStrokeOnBody(dynamic part, dynamic mainBody, out Exception error)
        {
            error = null;
            try
            {
                dynamic hsf = part.HybridShapeFactory;
                dynamic document = part.Parent;
                dynamic spaWorkbench = document.GetWorkbench("SPAWorkbench");

                DeleteGeometricalSet(part, TempSetName);
                dynamic gs = part.HybridBodies.Add();
                gs.Name = TempSetName;

                dynamic refBody = part.CreateReferenceFromObject(mainBody);

                // Standard box axes
                dynamic lineX = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(-10000, 0, 0), hsf.AddNewPointCoord(10000, 0, 0));
                dynamic lineY = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(0, -10000, 0), hsf.AddNewPointCoord(0, 10000, 0));
                dynamic lineZ = hsf.AddNewLinePtPt(hsf.AddNewPointCoord(0, 0, -10000), hsf.AddNewPointCoord(0, 0, 10000));
                gs.AppendHybridShape(lineX);
                gs.AppendHybridShape(lineY);
                gs.AppendHybridShape(lineZ);

                // The 2 extremums of W (direction Y), BBox-macro style
                dynamic extMax = BuildBoxExtremum(hsf, refBody, 1, lineY, lineZ, lineX);
                dynamic extMin = BuildBoxExtremum(hsf, refBody, 0, lineY, lineZ, lineX);
                gs.AppendHybridShape(extMax);
                gs.AppendHybridShape(extMin);

                // The 2 limit planes of W
                dynamic planeMax = hsf.AddNewPlaneNormal(lineY, extMax);
                dynamic planeMin = hsf.AddNewPlaneNormal(lineY, extMin);
                gs.AppendHybridShape(planeMax);
                gs.AppendHybridShape(planeMin);
                part.Update();

                // W = distance between the 2 planes
                dynamic measurable = spaWorkbench.GetMeasurable(planeMax);
                double width = Convert.ToDouble(measurable.GetMinimumDistance(planeMin), CultureInfo.InvariantCulture);

                dynamic parameters = part.Parameters;
                dynamic stroke = TryItemByName(parameters, PStroke);
                if (stroke == null)
                    parameters.CreateDimension(PStroke, "LENGTH", width);
                else
                    stroke.Value = width;

                DeleteGeometricalSet(part, TempSetName);
                part.Update();
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                try { DeleteGeometricalSet(part, TempSetName); } catch { }
                return false;
            }
        }

        /// <summary>Native CATIA body picker (VBA SelectBodyFromUser). Blocks
        /// until the user picks a body or cancels in CATIA.</summary>
        public static bool SelectBodyFromUser(dynamic destinationDocument, string prompt,
                                              out dynamic body, out string error)
        {
            body = null;
            error = "";
            dynamic selection = null;
            try { selection = destinationDocument.Selection; } catch { }
            if (selection == null) { error = "The CATIA selection is not available."; return false; }

            try { selection.Clear(); } catch { }

            string result = "";
            try
            {
                result = Convert.ToString(selection.SelectElement2(new object[] { "Body" }, prompt, false),
                    CultureInfo.InvariantCulture) ?? "";
            }
            catch (Exception ex)
            {
                error = FriendlyCatiaError(ex);
                return false;
            }

            if (!string.Equals(result, "Normal", StringComparison.OrdinalIgnoreCase))
            {
                error = "No body was selected.";
                return false;
            }

            try { body = selection.Item(1).Value; }
            catch { error = "The selected body could not be read."; return false; }

            try { selection.Clear(); } catch { }

            if (body == null) { error = "The selected body could not be read."; return false; }
            if (GetName(body).Trim().Length == 0) { body = null; error = "The selected body has no name."; return false; }
            return true;
        }

        // ==============================================================
        // Injection-molding rules (STROKE link + Draft formula)
        // ==============================================================

        /// <summary>Links every LIFTER_HEAD STROKE_Distance to the root parameter
        /// (VBA LinkStrokeToMainBody). Idempotent; returns the links created.</summary>
        public static int LinkStrokeToMainBody(dynamic part)
        {
            int linked = 0;
            try
            {
                dynamic parameters = part.Parameters;
                dynamic rootStroke = TryItemByName(parameters, PStroke);
                if (rootStroke == null) return 0;

                string rootName = RelationName(part, rootStroke);
                if (string.IsNullOrWhiteSpace(rootName)) return 0;

                dynamic relations = null;
                try { relations = part.Relations; } catch { }
                if (relations == null) return 0;

                foreach (dynamic headSet in GetHeadSets(part))
                {
                    dynamic strokeParameter = FindDirect(headSet, PStroke);
                    if (strokeParameter == null) continue;
                    try
                    {
                        relations.CreateFormula("", "", strokeParameter, rootName);
                        linked++;
                    }
                    catch { }
                }
                try { part.Update(); } catch { }
            }
            catch { }
            return linked;
        }

        /// <summary>Creates/refreshes the Draft angle formula on every LIFTER_HEAD:
        /// min( floor( atan( ( UNDERCUT_LENGTH + 5mm ) / STROKE_Distance ) * 180/PI + 0.5 ), 15 ) * 1deg
        /// (VBA CreateDraftParameters). Existing Draft formulas are replaced.</summary>
        public static int CreateDraftParameters(dynamic part)
        {
            int created = 0;
            try
            {
                dynamic relations = null;
                try { relations = part.Relations; } catch { }
                if (relations == null) return 0;

                foreach (dynamic headSet in GetHeadSets(part))
                {
                    dynamic undercut = FindDirect(headSet, P5);
                    dynamic stroke = FindDirect(headSet, PStroke);
                    if (undercut == null || stroke == null) continue;

                    dynamic draft = FindDirect(headSet, PDraft);
                    if (draft == null)
                    {
                        try { draft = headSet.DirectParameters.CreateDimension(PDraft, "ANGLE", 0); }
                        catch { draft = null; }
                    }
                    if (draft == null) continue;

                    string undercutName = RelationName(part, undercut);
                    string strokeName = RelationName(part, stroke);
                    if (undercutName.Length == 0 || strokeName.Length == 0) continue;

                    string formulaText = "min( floor( atan( ( " + undercutName + " + 5mm ) / " + strokeName +
                                         " ) * 180 / PI + 0.5 ), " +
                                         DraftMaxDeg.ToString(CultureInfo.InvariantCulture) + " ) * 1deg";

                    dynamic oldFormula = GetFormulaFor(part, draft);
                    if (oldFormula != null)
                    {
                        try { relations.Remove(oldFormula.Name); } catch { }
                    }

                    try
                    {
                        relations.CreateFormula("", "", draft, formulaText);
                        created++;
                    }
                    catch { }
                }
                try { part.Update(); } catch { }
            }
            catch { }
            return created;
        }

        /// <summary>Returns the Formula relation driving a parameter (VBA
        /// GetFormulaFor). LeftPart is probed defensively: relations without a
        /// LeftPart (DesignTable, Law...) are skipped, replacing TypeName check.</summary>
        private static dynamic GetFormulaFor(dynamic part, dynamic drivenParameter)
        {
            dynamic relations = null;
            try { relations = part.Relations; } catch { }
            int count = TryCount(relations);
            if (count == 0) return null;

            string drivenName = RelationName(part, drivenParameter);
            string drivenLeaf = LeafName(GetName(drivenParameter));

            for (int i = 1; i <= count; i++)
            {
                dynamic relation = TryItemByIndex(relations, i);
                if (relation == null) continue;

                dynamic leftPart = null;
                try { leftPart = relation.LeftPart; } catch { }
                if (leftPart == null) continue;      // not a Formula

                string leftName = RelationName(part, leftPart);
                if (drivenName.Length > 0 && leftName.Length > 0)
                {
                    if (string.Equals(leftName, drivenName, StringComparison.OrdinalIgnoreCase))
                        return relation;
                    continue;
                }
                if (leftName.Length == 0)
                {
                    string leftLeaf = LeafName(GetName(leftPart));
                    if (leftLeaf.Length > 0 && drivenLeaf.Length > 0 &&
                        string.Equals(leftLeaf, drivenLeaf, StringComparison.OrdinalIgnoreCase))
                        return relation;
                }
            }
            return null;
        }

        /// <summary>Replicates the CATIA Draft formula on two numeric values (mm).</summary>
        public static double ComputeDraftValue(double undercutMm, double strokeMm)
        {
            double raw = Math.Atan((undercutMm + 5.0) / strokeMm) * 180.0 / Math.PI;
            raw = Math.Floor(raw + 0.5);
            if (raw > DraftMaxDeg) raw = DraftMaxDeg;
            return raw;
        }

        /// <summary>Draft readout (VBA GetDraftDisplay): compute from the formula
        /// inputs first, fall back to the Draft parameter, else "-".</summary>
        public static void GetDraftDisplay(dynamic part, dynamic headSet,
                                           out string draftText, out string draftSource)
        {
            draftText = "-";
            draftSource = "draft not available";
            try
            {
                dynamic rootStroke = TryItemByName(RootParameters(part), PStroke);
                dynamic undercut = null, stroke = null, draft = null;
                if (headSet != null)
                {
                    undercut = FindDirect(headSet, P5);
                    stroke = FindDirect(headSet, PStroke);
                    draft = FindDirect(headSet, PDraft);
                }

                double undercutValue, strokeValue;
                bool undercutOk = NumericValue(undercut, out undercutValue);
                bool strokeOk = NumericValue(stroke ?? rootStroke, out strokeValue);

                if (undercutOk && strokeOk && strokeValue > 0)
                {
                    draftText = ComputeDraftValue(undercutValue, strokeValue)
                        .ToString("0.###", CultureInfo.InvariantCulture);
                    draftSource = "draft from formula";
                    return;
                }

                if (draft != null)
                {
                    string value = ValueOnly(draft);
                    if (value.Length > 0)
                    {
                        draftText = value;
                        draftSource = "draft from parameter";
                    }
                }
            }
            catch
            {
                draftText = "-";
                draftSource = "draft not available";
            }
        }

        // ==============================================================
        // Instance read / write (VBA SendValues + UpdateValues)
        // ==============================================================

        /// <summary>Reads one instance. status: OK | NOT_FOUND | INCOMPLETE.</summary>
        public static LifterInstanceSnapshot ReadInstance(dynamic part, int instanceIndex, out string status)
        {
            status = "NOT_FOUND";
            var snapshot = new LifterInstanceSnapshot();
            int index = instanceIndex < 1 ? 1 : instanceIndex;
            snapshot.Index = index;

            dynamic undercutSet = GetUndercutSet(part, index);
            dynamic headSet = GetHeadSet(part, index);
            if (undercutSet == null || headSet == null) return snapshot;

            dynamic a = FindDirect(undercutSet, P1);
            dynamic b = FindDirect(undercutSet, P2);
            dynamic c = FindDirect(undercutSet, P3);
            dynamic d = FindDirect(undercutSet, P4);
            dynamic e = FindDirect(headSet, P5);

            if (a == null || b == null || c == null || d == null || e == null)
            {
                status = "INCOMPLETE";
                return snapshot;
            }

            snapshot.UpperInternalLength = ValueOnly(a);
            snapshot.InternalVerticalAngle = ValueOnly(b);
            snapshot.InternalHorizontalAngle = ValueOnly(c);
            snapshot.HasExternalFace = BooleanText(d);
            snapshot.UndercutLength = ValueOnly(e);

            snapshot.Stroke = GetStrokeText(part);
            string draftText, draftSource;
            GetDraftDisplay(part, headSet, out draftText, out draftSource);
            snapshot.Draft = draftText;
            snapshot.DraftSource = draftSource;

            status = "OK";
            return snapshot;
        }

        /// <summary>Writes the five editable parameters of one instance
        /// (VBA UpdateValues). Throws InvalidOperationException with a
        /// friendly message on failure.</summary>
        public static void UpdateInstance(dynamic part, int instanceIndex, LifterInstanceSnapshot values)
        {
            dynamic undercutSet = GetUndercutSet(part, instanceIndex);
            dynamic headSet = GetHeadSet(part, instanceIndex);
            if (undercutSet == null)
                throw new InvalidOperationException("The selected UNDERCUT_DEPTH instance was not found in the CATPart.");
            if (headSet == null)
                throw new InvalidOperationException("The matching LIFTER_HEAD parameter set was not found in the CATPart.");

            dynamic a = FindDirect(undercutSet, P1);
            dynamic b = FindDirect(undercutSet, P2);
            dynamic c = FindDirect(undercutSet, P3);
            dynamic d = FindDirect(undercutSet, P4);
            dynamic e = FindDirect(headSet, P5);
            if (a == null || b == null || c == null)
                throw new InvalidOperationException("An UNDERCUT_DEPTH parameter is missing (distances / angles).");
            if (d == null || e == null)
                throw new InvalidOperationException("HAS_EXTERNAL_FACE or UNDERCUT_LENGTH is missing on this instance.");

            ValuateDimension(a, values.UpperInternalLength, "mm", "Upper internal distance");
            ValuateDimension(b, values.InternalVerticalAngle, "deg", "Internal vertical angle");
            ValuateDimension(c, values.InternalHorizontalAngle, "deg", "Internal horizontal angle");
            ValuateBoolean(d, values.HasExternalFace);
            ValuateDimension(e, values.UndercutLength, "mm", "Lifter head length");

            try { part.Update(); } catch { }
        }

        private static void ValuateDimension(dynamic parameter, string rawValue, string unitName, string label)
        {
            string text = (rawValue ?? "").Trim();
            if (text.Length == 0)
                throw new InvalidOperationException("The " + label + " value is empty.");
            text = text.Replace(',', '.');
            try
            {
                parameter.ValuateFromString(text + unitName);
                return;
            }
            catch { }
            throw new InvalidOperationException(label + " could not be updated. Enter a plain number (e.g. 12.5).");
        }

        private static void ValuateBoolean(dynamic parameter, string rawValue)
        {
            string text = (rawValue ?? "").Trim().ToLowerInvariant();
            if (text != "true" && text != "false")
                throw new InvalidOperationException("Invalid Boolean value for HAS_EXTERNAL_FACE.");
            try
            {
                parameter.Value = text == "true";
                return;
            }
            catch { }
            try
            {
                parameter.ValuateFromString(text);
                return;
            }
            catch { }
            throw new InvalidOperationException("HAS_EXTERNAL_FACE could not be updated.");
        }

        // ==============================================================
        // Boolean Remove engine (VBA RunBooleanRemoveCore)
        // ==============================================================

        /// <summary>True when the stored body reference still lives in this part's
        /// Bodies collection (VBA IsBodyObjectUsable, name-based out of process).</summary>
        public static bool IsBodyUsable(dynamic part, dynamic body)
        {
            if (body == null) return false;
            string name = GetName(body);
            if (name.Length == 0) return false;             // dead COM reference

            dynamic bodies = null;
            try { bodies = part.Bodies; } catch { }
            int count = TryCount(bodies);
            for (int i = 1; i <= count; i++)
            {
                dynamic candidate = TryItemByIndex(bodies, i);
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, body)) return true;
                if (string.Equals(GetName(candidate), name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Identity test for two bodies (VBA AreSameCATIAObject,
        /// name-based because out-of-process COM hands out fresh proxies).</summary>
        public static bool AreSameBody(dynamic part, dynamic first, dynamic second)
        {
            if (first == null || second == null) return false;
            if (ReferenceEquals(first, second)) return true;
            string a = GetName(first);
            string b = GetName(second);
            return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Copy tool body -> Paste Special "As Result, with out link" ->
        /// rename REMOVE_TOOL_Ixxx -> ShapeFactory.AddNewRemove -> Remove_Ixxx
        /// (VBA RunBooleanRemoveCore). Nothing here touches the ORIGINAL tool
        /// body - only the pasted copy is consumed.</summary>
        public static LifterRemoveResult RunBooleanRemove(dynamic part, dynamic destinationDocument,
                                                          dynamic sourceBody, dynamic targetBody,
                                                          int instanceIndex)
        {
            var result = new LifterRemoveResult();
            string tag = instanceIndex.ToString("000", CultureInfo.InvariantCulture);
            dynamic savedWorkObject = null;

            try
            {
                if (sourceBody == null)
                {
                    result.Message = "No copy body is stored for Instance " + tag + ". Select the copy body first.";
                    return result;
                }
                if (targetBody == null)
                {
                    result.Message = "No target body is stored for Instance " + tag + ". Select the target body first.";
                    return result;
                }
                if (AreSameBody(part, sourceBody, targetBody))
                {
                    result.Message = "Copy body and target body cannot be the same body (Instance " + tag + ").";
                    return result;
                }
                if (!IsBodyUsable(part, sourceBody))
                {
                    result.DeadSlot = "copy";
                    result.Message = "The stored copy body of Instance " + tag +
                                     " is no longer available. Select the copy body again.";
                    return result;
                }
                if (!IsBodyUsable(part, targetBody))
                {
                    result.DeadSlot = "target";
                    result.Message = "The stored target body of Instance " + tag +
                                     " is no longer available. Select the target body again.";
                    return result;
                }

                dynamic bodies = part.Bodies;
                dynamic selection = destinationDocument.Selection;
                int countBefore = TryCount(bodies);

                // ----- copy the tool body -----
                selection.Clear();
                selection.Add(sourceBody);
                selection.Copy();

                // ----- paste it into the target as a standalone result -----
                selection.Clear();
                selection.Add(targetBody);
                if (!PasteBodyAsResult(selection))
                    throw new InvalidOperationException("CATIA could not paste the selected copy body as a result.");

                try { part.Update(); } catch { }
                if (TryCount(bodies) <= countBefore)
                    throw new InvalidOperationException("No new result body was created by Paste Special.");

                dynamic resultBody = FindNewBodyAfterPaste(bodies, countBefore, sourceBody, targetBody, selection);
                if (resultBody == null)
                    throw new InvalidOperationException("The pasted result body could not be identified.");

                // ----- name the tool body, keep the CATIA name if renaming fails -----
                string toolName = GetUniqueBodyName(bodies, "REMOVE_TOOL_I" + tag);
                try { resultBody.Name = toolName; }
                catch { toolName = GetName(resultBody); }

                // ----- cut it out of the target body -----
                try { savedWorkObject = part.InWorkObject; } catch { }
                part.InWorkObject = targetBody;

                dynamic shapeFactory = part.ShapeFactory;
                dynamic removeFeature = shapeFactory.AddNewRemove(resultBody);

                string removeName = GetUniqueFeatureName(targetBody, "Remove_I" + tag);
                string featureName = removeName;
                try { removeFeature.Name = removeName; }
                catch
                {
                    featureName = "Remove_I" + tag + "_" +
                                  DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
                    try { removeFeature.Name = featureName; } catch { }
                }

                // ----- leave the part the way it was found -----
                if (savedWorkObject != null) { try { part.InWorkObject = savedWorkObject; } catch { } }
                try { part.Update(); } catch { }

                try
                {
                    selection.Clear();
                    selection.Add(removeFeature);
                }
                catch { }

                result.Ok = true;
                result.FeatureName = featureName;
                result.Message = "Boolean Remove completed for Instance " + tag +
                                 ". Source: " + GetName(sourceBody) +
                                 " | Target: " + GetName(targetBody) +
                                 " | Feature: " + featureName;
            }
            catch (Exception ex)
            {
                if (savedWorkObject != null) { try { part.InWorkObject = savedWorkObject; } catch { } }
                try { destinationDocument.Selection.Clear(); } catch { }
                result.Ok = false;
                result.Message = "Boolean Remove failed for Instance " + tag + ": " + ex.Message;
            }
            return result;
        }

        /// <summary>Paste Special as result without link, with the defensive
        /// fallbacks of the VBA PasteBodyAsResult.</summary>
        private static bool PasteBodyAsResult(dynamic selection)
        {
            try { selection.PasteSpecial(PasteModeNoLink); return true; } catch { }
            try { selection.PasteSpecialMode = 2; } catch { }
            try { selection.PasteSpecial(); return true; } catch { }
            return false;
        }

        /// <summary>Locates the body created by Paste Special (VBA
        /// FindNewBodyAfterPaste): the newest body that is neither the tool
        /// nor the target, with the current selection as fallback.</summary>
        private static dynamic FindNewBodyAfterPaste(dynamic bodies, int countBefore,
                                                     dynamic sourceBody, dynamic targetBody,
                                                     dynamic selection)
        {
            string sourceName = GetName(sourceBody);
            string targetName = GetName(targetBody);

            for (int i = TryCount(bodies); i >= countBefore + 1; i--)
            {
                dynamic candidate = TryItemByIndex(bodies, i);
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, sourceBody) || ReferenceEquals(candidate, targetBody)) continue;
                string candidateName = GetName(candidate);
                if (string.Equals(candidateName, sourceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase)) continue;
                return candidate;
            }

            try
            {
                if (TryCount(selection) > 0)
                {
                    dynamic candidate = selection.Item(1).Value;
                    if (candidate != null)
                    {
                        if (ReferenceEquals(candidate, sourceBody) || ReferenceEquals(candidate, targetBody)) return null;
                        string candidateName = GetName(candidate);
                        if (string.Equals(candidateName, sourceName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(candidateName, targetName, StringComparison.OrdinalIgnoreCase)) return null;
                        return candidate;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>First free REMOVE_TOOL_I001 / _2 / _3 ... name (VBA GetUniqueBodyName).</summary>
        private static string GetUniqueBodyName(dynamic bodies, string baseName)
        {
            string candidate = baseName;
            int suffix = 1;
            while (TryItemByName(bodies, candidate) != null)
            {
                suffix++;
                candidate = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            }
            return candidate;
        }

        /// <summary>First free Remove_I001 / _2 / _3 ... name among the target
        /// body's shapes (VBA GetUniqueFeatureName, using the real Body.Shapes
        /// collection).</summary>
        private static string GetUniqueFeatureName(dynamic targetBody, string baseName)
        {
            dynamic shapes = null;
            try { shapes = targetBody.Shapes; } catch { }
            int count = TryCount(shapes);

            string candidate = baseName;
            int suffix = 1;
            while (true)
            {
                bool taken = false;
                for (int i = 1; i <= count; i++)
                {
                    dynamic shape = TryItemByIndex(shapes, i);
                    if (shape == null) continue;
                    if (string.Equals(GetName(shape), candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        taken = true;
                        break;
                    }
                }
                if (!taken) return candidate;
                suffix++;
                candidate = baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ==============================================================
        // Friendly CATIA errors
        // ==============================================================

        public static string FriendlyCatiaError(Exception ex)
        {
            if (ex == null) return "Unknown CATIA error.";
            string message = (ex.Message ?? string.Empty).Trim();
            if (message.Length == 0) message = ex.GetType().Name;

            COMException com = ex as COMException;
            int hresult = com != null ? com.HResult : 0;
            bool callRejected =
                hresult == unchecked((int)0x80010001) ||      // RPC_E_CALL_REJECTED
                hresult == unchecked((int)0x8001010A) ||      // RPC_E_SERVERCALL_RETRYLATER
                message.IndexOf("call rejected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("server busy", StringComparison.OrdinalIgnoreCase) >= 0;
            if (callRejected)
                return "CATIA is busy (a native command or dialog is probably open). Close it and try again.";

            bool connectionLost =
                message.IndexOf("RPC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("remote procedure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("not running", StringComparison.OrdinalIgnoreCase) >= 0;
            if (connectionLost)
                return "The connection to CATIA was lost. Check that CATIA and the CATPart are still open.";

            return message;
        }
    }
}
