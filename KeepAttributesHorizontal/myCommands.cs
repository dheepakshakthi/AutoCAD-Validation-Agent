using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

//namespace EditorInput has been used for displaying the message on AutoCAD command prompt using ed.WriteMessage() 
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using KeepAttributesHorizontal.UI;

namespace KeepAttributesHorizontal
{
    // This class is instantiated by AutoCAD for each document when
    // a command is called by the user the first time in the context
    // of a given document. In other words, non static data in this class
    // is implicitly per-document!
    public class MyCommands
    {

        //Our custom overrule class derived from TransformOverrule

        public class KeepStraightOverrule : TransformOverrule
        {
            //We want to change how an AttributeReference responds to being
            //transformed (moved, rotated, etc.), so we override its
            //standard TransformBy function.
            public override void TransformBy(Entity entity, Matrix3d transform)
            {
                //Call the normal TransformBy function for the attribute reference
                //we're overruling.
                base.TransformBy(entity, transform);

                //We know entity must be an AttributeReference because 
                //that is the only entity we registered the overrule for.
                AttributeReference attRef = (AttributeReference)entity;

                //Set rotation of attribute reference to 0(horizontal)
                attRef.Rotation = 0.0;
            }
        }


        //Class variable to store the instance of our overrule
        //declaring myOverRule as nullable to avoid warning CS8618: Non-nullable field 'myOverRule' must contain a non-null value when exiting constructor. Consider declaring the field as nullable.
        static KeepStraightOverrule? myOverRule;

        // --- NEW VARIABLES FOR UI ---
        static PaletteSet? myAiPaletteSet;
        static AiAssistantControl? myAiControl;

        [CommandMethod("ShowAiAssistant")]
        public static void ShowAiAssistant()
        {
            if (myAiPaletteSet == null)
            {
                // Create the palette set
                myAiPaletteSet = new PaletteSet("VARROC AI Assistant");

                // Set its docking property to dockable
                myAiPaletteSet.Style = PaletteSetStyles.ShowPropertiesMenu |
                                       PaletteSetStyles.ShowAutoHideButton |
                                       PaletteSetStyles.ShowCloseButton;
                
                myAiPaletteSet.MinimumSize = new System.Drawing.Size(300, 400);

                // Create the WPF User Control
                myAiControl = new AiAssistantControl();

                // Add the WPF User Control directly using AddVisual method
                myAiPaletteSet.AddVisual("AI Design Validation", myAiControl);
            }

            // Display the palette set
            myAiPaletteSet.Visible = true;
        }

        [CommandMethod("KeepStraight")]

        public static void ImplementOverrule()
        {
            Editor ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;

            //We only want to create our overrule instance once, 
            //so we check if it already exists before we create it
            //(i.e. this may be the 2nd time we've run the command)
            if (myOverRule == null)
            {
                //Instantiate our overrule class
                myOverRule = new KeepStraightOverrule();

                //Register the overrule
                TransformOverrule.AddOverrule(RXClass.GetClass(typeof(AttributeReference)), myOverRule, false);
            }

            //Make sure overruling is turned on so our overrule works
            TransformOverrule.Overruling = true;
            ed.WriteMessage("\nAttributes are now parallel to x-axis\n");
        }

    }

}
